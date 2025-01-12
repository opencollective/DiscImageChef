﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : SSC.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Core algorithms.
//
// --[ Description ] ----------------------------------------------------------
//
//     Dumps media from SCSI Streaming devices.
//
// --[ License ] --------------------------------------------------------------
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as
//     published by the Free Software Foundation, either version 3 of the
//     License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright © 2011-2019 Natalia Portillo
// ****************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Extents;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.CommonTypes.Metadata;
using DiscImageChef.CommonTypes.Structs;
using DiscImageChef.Core.Logging;
using DiscImageChef.Decoders.SCSI;
using DiscImageChef.Devices;
using Schemas;
using MediaType = DiscImageChef.CommonTypes.MediaType;

namespace DiscImageChef.Core.Devices.Dumping
{
    partial class Dump
    {
        /// <summary>
        ///     Dumps the tape from a SCSI Streaming device
        /// </summary>
        internal void Ssc()
        {
            FixedSense? fxSense;
            bool        sense;
            uint        blockSize;
            ulong       blocks  = 0;
            MediaType   dskType = MediaType.Unknown;
            DateTime    start;
            DateTime    end;
            double      totalDuration = 0;
            double      currentSpeed  = 0;
            double      maxSpeed      = double.MinValue;
            double      minSpeed      = double.MaxValue;

            dev.RequestSense(out byte[] senseBuf, dev.Timeout, out double duration);
            fxSense = Sense.DecodeFixed(senseBuf, out string strSense);

            InitProgress?.Invoke();
            if(fxSense.HasValue && fxSense?.SenseKey != SenseKeys.NoSense)
            {
                dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                  fxSense?.ASC, fxSense?.ASCQ);
                StoppingErrorMessage?.Invoke("Drive has status error, please correct. Sense follows..." +
                                             Environment.NewLine                                        + strSense);
                return;
            }

            // Not in BOM/P
            if(fxSense.HasValue && fxSense?.ASC == 0x00 && fxSense?.ASCQ != 0x00 && fxSense?.ASCQ != 0x04 &&
               fxSense?.SenseKey                != SenseKeys.IllegalRequest)
            {
                dumpLog.WriteLine("Rewinding, please wait...");
                PulseProgress?.Invoke("Rewinding, please wait...");
                // Rewind, let timeout apply
                dev.Rewind(out senseBuf, dev.Timeout, out duration);

                // Still rewinding?
                // TODO: Pause?
                do
                {
                    PulseProgress?.Invoke("Rewinding, please wait...");
                    dev.RequestSense(out senseBuf, dev.Timeout, out duration);
                    fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                }
                while(fxSense.HasValue && fxSense?.ASC == 0x00 &&
                      (fxSense?.ASCQ == 0x1A || fxSense?.ASCQ != 0x04 || fxSense?.ASCQ != 0x00));

                dev.RequestSense(out senseBuf, dev.Timeout, out duration);
                fxSense = Sense.DecodeFixed(senseBuf, out strSense);

                // And yet, did not rewind!
                if(fxSense.HasValue && (fxSense?.ASC == 0x00 && fxSense?.ASCQ != 0x04 && fxSense?.ASCQ != 0x00 ||
                                        fxSense?.ASC != 0x00))
                {
                    StoppingErrorMessage?.Invoke("Drive could not rewind, please correct. Sense follows..." +
                                                 Environment.NewLine                                        + strSense);
                    dumpLog.WriteLine("Drive could not rewind, please correct. Sense follows...");
                    dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                      fxSense?.ASC, fxSense?.ASCQ);
                    return;
                }
            }

            // Check position
            sense = dev.ReadPosition(out byte[] cmdBuf, out senseBuf, SscPositionForms.Short, dev.Timeout,
                                     out duration);

            if(sense)
            {
                // READ POSITION is mandatory starting SCSI-2, so do not cry if the drive does not recognize the command (SCSI-1 or earlier)
                // Anyway, <=SCSI-1 tapes do not support partitions
                fxSense = Sense.DecodeFixed(senseBuf, out strSense);

                if(fxSense.HasValue && (fxSense?.ASC == 0x20 && fxSense?.ASCQ     != 0x00 ||
                                        fxSense?.ASC != 0x20 && fxSense?.SenseKey != SenseKeys.IllegalRequest))
                {
                    StoppingErrorMessage?.Invoke("Could not get position. Sense follows..." + Environment.NewLine +
                                                 strSense);
                    dumpLog.WriteLine("Could not get position. Sense follows...");
                    dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                      fxSense?.ASC, fxSense?.ASCQ);
                    return;
                }
            }
            else
            {
                // Not in partition 0
                if(cmdBuf[1] != 0)
                {
                    UpdateStatus?.Invoke("Drive not in partition 0. Rewinding, please wait...");
                    dumpLog.WriteLine("Drive not in partition 0. Rewinding, please wait...");
                    // Rewind, let timeout apply
                    sense = dev.Locate(out senseBuf, false, 0, 0, dev.Timeout, out duration);
                    if(sense)
                    {
                        StoppingErrorMessage?.Invoke("Drive could not rewind, please correct. Sense follows..." +
                                                     Environment.NewLine                                        +
                                                     strSense);
                        dumpLog.WriteLine("Drive could not rewind, please correct. Sense follows...");
                        dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                          fxSense?.ASC, fxSense?.ASCQ);
                        return;
                    }

                    // Still rewinding?
                    // TODO: Pause?
                    do
                    {
                        Thread.Sleep(1000);
                        PulseProgress?.Invoke("Rewinding, please wait...");
                        dev.RequestSense(out senseBuf, dev.Timeout, out duration);
                        fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                    }
                    while(fxSense.HasValue && fxSense?.ASC == 0x00 && (fxSense?.ASCQ == 0x1A || fxSense?.ASCQ == 0x19));

                    // And yet, did not rewind!
                    if(fxSense.HasValue && (fxSense?.ASC == 0x00 && fxSense?.ASCQ != 0x04 && fxSense?.ASCQ != 0x00 ||
                                            fxSense?.ASC != 0x00))
                    {
                        StoppingErrorMessage?.Invoke("Drive could not rewind, please correct. Sense follows..." +
                                                     Environment.NewLine                                        +
                                                     strSense);
                        dumpLog.WriteLine("Drive could not rewind, please correct. Sense follows...");
                        dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                          fxSense?.ASC, fxSense?.ASCQ);
                        return;
                    }

                    sense = dev.ReadPosition(out cmdBuf, out senseBuf, SscPositionForms.Short, dev.Timeout,
                                             out duration);
                    if(sense)
                    {
                        fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                        StoppingErrorMessage?.Invoke("Drive could not rewind, please correct. Sense follows..." +
                                                     Environment.NewLine                                        +
                                                     strSense);
                        dumpLog.WriteLine("Drive could not rewind, please correct. Sense follows...");
                        dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                          fxSense?.ASC, fxSense?.ASCQ);
                        return;
                    }

                    // Still not in partition 0!!!?
                    if(cmdBuf[1] != 0)
                    {
                        StoppingErrorMessage?.Invoke("Drive could not rewind to partition 0 but no error occurred...");
                        dumpLog.WriteLine("Drive could not rewind to partition 0 but no error occurred...");
                        return;
                    }
                }
            }

            EndProgress?.Invoke();

            byte   scsiMediumTypeTape  = 0;
            byte   scsiDensityCodeTape = 0;
            byte[] mode6Data           = null;
            byte[] mode10Data          = null;

            UpdateStatus?.Invoke("Requesting MODE SENSE (10).");
            sense = dev.ModeSense10(out cmdBuf, out senseBuf, false, true, ScsiModeSensePageControl.Current, 0x3F, 0xFF,
                                    5, out duration);
            if(!sense || dev.Error)
                sense = dev.ModeSense10(out cmdBuf, out senseBuf, false, true, ScsiModeSensePageControl.Current, 0x3F,
                                        0x00, 5, out duration);

            Modes.DecodedMode? decMode = null;

            if(!sense && !dev.Error)
                if(Modes.DecodeMode10(cmdBuf, dev.ScsiType).HasValue)
                    decMode = Modes.DecodeMode10(cmdBuf, dev.ScsiType);

            UpdateStatus?.Invoke("Requesting MODE SENSE (6).");
            sense = dev.ModeSense6(out cmdBuf, out senseBuf, false, ScsiModeSensePageControl.Current, 0x3F, 0x00, 5,
                                   out duration);
            if(sense || dev.Error)
                sense = dev.ModeSense6(out cmdBuf, out senseBuf, false, ScsiModeSensePageControl.Current, 0x3F, 0x00, 5,
                                       out duration);
            if(sense || dev.Error) sense = dev.ModeSense(out cmdBuf, out senseBuf, 5, out duration);

            if(!sense && !dev.Error)
                if(Modes.DecodeMode6(cmdBuf, dev.ScsiType).HasValue)
                    decMode = Modes.DecodeMode6(cmdBuf, dev.ScsiType);

            // TODO: Check partitions page
            if(decMode.HasValue)
            {
                scsiMediumTypeTape = (byte)decMode.Value.Header.MediumType;
                if(decMode.Value.Header.BlockDescriptors != null && decMode.Value.Header.BlockDescriptors.Length >= 1)
                    scsiDensityCodeTape = (byte)decMode.Value.Header.BlockDescriptors[0].Density;
                blockSize = decMode.Value.Header.BlockDescriptors?[0].BlockLength ?? 0;

                UpdateStatus?.Invoke($"Device reports {blocks} blocks ({blocks * blockSize} bytes).");
            }
            else blockSize = 1;

            if(blockSize == 0) blockSize = 1;

            if(dskType == MediaType.Unknown)
                dskType = MediaTypeFromScsi.Get((byte)dev.ScsiType, dev.Manufacturer, dev.Model, scsiMediumTypeTape,
                                                scsiDensityCodeTape, blocks, blockSize);
            if(dskType == MediaType.Unknown) dskType = MediaType.UnknownTape;

            UpdateStatus?.Invoke($"SCSI device type: {dev.ScsiType}.");
            UpdateStatus?.Invoke($"SCSI medium type: {scsiMediumTypeTape}.");
            UpdateStatus?.Invoke($"SCSI density type: {scsiDensityCodeTape}.");
            UpdateStatus?.Invoke($"Media identified as {dskType}.");

            dumpLog.WriteLine("SCSI device type: {0}.",   dev.ScsiType);
            dumpLog.WriteLine("SCSI medium type: {0}.",   scsiMediumTypeTape);
            dumpLog.WriteLine("SCSI density type: {0}.",  scsiDensityCodeTape);
            dumpLog.WriteLine("Media identified as {0}.", dskType);

            bool  endOfMedia       = false;
            ulong currentBlock     = 0;
            uint  currentFile      = 0;
            byte  currentPartition = 0;
            byte  totalPartitions  = 1; // TODO: Handle partitions.
            bool  fixedLen         = false;
            uint  transferLen      = blockSize;

            firstRead:
            sense = dev.Read6(out cmdBuf, out senseBuf, false, fixedLen, transferLen, blockSize, dev.Timeout,
                              out duration);
            if(sense)
            {
                fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                if(fxSense.HasValue)
                    if(fxSense?.SenseKey == SenseKeys.IllegalRequest)
                    {
                        sense = dev.Space(out senseBuf, SscSpaceCodes.LogicalBlock, -1, dev.Timeout, out duration);
                        if(sense)
                        {
                            fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                            if(!fxSense.HasValue || !fxSense?.EOM == true)
                            {
                                StoppingErrorMessage?.Invoke("Drive could not return back. Sense follows..." +
                                                             Environment.NewLine                             +
                                                             strSense);
                                dumpLog.WriteLine("Drive could not return back. Sense follows...");
                                dumpLog.WriteLine("Device not ready. Sense {0} ASC {1:X2}h ASCQ {2:X2}h",
                                                  fxSense?.SenseKey, fxSense?.ASC, fxSense?.ASCQ);
                                return;
                            }
                        }

                        fixedLen    = true;
                        transferLen = 1;
                        sense = dev.Read6(out cmdBuf, out senseBuf, false, fixedLen, transferLen, blockSize,
                                          dev.Timeout, out duration);
                        if(sense)
                        {
                            fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                            StoppingErrorMessage?.Invoke("Drive could not read. Sense follows..." +
                                                         Environment.NewLine                      + strSense);
                            dumpLog.WriteLine("Drive could not read. Sense follows...");
                            dumpLog.WriteLine("Device not ready. Sense {0} ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                              fxSense?.ASC, fxSense?.ASCQ);
                            return;
                        }
                    }
                    else if(fxSense?.ASC              == 0x00 && fxSense?.ASCQ == 0x00 && fxSense?.ILI == true &&
                            fxSense?.InformationValid == true)
                    {
                        blockSize = (uint)((int)blockSize -
                                           BitConverter.ToInt32(BitConverter.GetBytes(fxSense.Value.Information), 0));
                        transferLen = blockSize;

                        UpdateStatus?.Invoke($"Blocksize changed to {blockSize} bytes at block {currentBlock}");
                        dumpLog.WriteLine("Blocksize changed to {0} bytes at block {1}", blockSize, currentBlock);

                        sense = dev.Space(out senseBuf, SscSpaceCodes.LogicalBlock, -1, dev.Timeout,
                                                   out duration);
                        totalDuration += duration;

                        if(sense)
                        {
                            fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                            StoppingErrorMessage?.Invoke("Drive could not go back one block. Sense follows..." +
                                                         Environment.NewLine                                   +
                                                         strSense);
                            dumpLog.WriteLine("Drive could not go back one block. Sense follows...");
                            dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h",
                                              fxSense?.SenseKey, fxSense?.ASC, fxSense?.ASCQ);
                            return;
                        }

                        goto firstRead;
                    }
                    else
                    {
                        StoppingErrorMessage?.Invoke("Drive could not read. Sense follows..." + Environment.NewLine +
                                                     strSense);
                        dumpLog.WriteLine("Drive could not read. Sense follows...");
                        dumpLog.WriteLine("Device not ready. Sense {0} ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                          fxSense?.ASC, fxSense?.ASCQ);
                        return;
                    }
                else
                {
                    StoppingErrorMessage?.Invoke("Cannot read device, don't know why, exiting...");
                    dumpLog.WriteLine("Cannot read device, don't know why, exiting...");
                    return;
                }
            }

            sense = dev.Space(out senseBuf, SscSpaceCodes.LogicalBlock, -1, dev.Timeout, out duration);
            if(sense)
            {
                fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                if(!fxSense.HasValue || !fxSense?.EOM == true)
                {
                    StoppingErrorMessage?.Invoke("Drive could not return back. Sense follows..." + Environment.NewLine +
                                                 strSense);
                    dumpLog.WriteLine("Drive could not return back. Sense follows...");
                    dumpLog.WriteLine("Device not ready. Sense {0} ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                      fxSense?.ASC, fxSense?.ASCQ);
                    return;
                }
            }

            DumpHardwareType currentTry = null;
            ExtentsULong     extents    = null;
            ResumeSupport.Process(true, dev.IsRemovable, blocks, dev.Manufacturer, dev.Model, dev.Serial,
                                  dev.PlatformId, ref resume, ref currentTry, ref extents, true);
            if(currentTry == null || extents == null)
            {
                StoppingErrorMessage?.Invoke("Could not process resume file, not continuing...");
                return;
            }

            bool canLocateLong = false;
            bool canLocate     = false;

            UpdateStatus?.Invoke("Positioning tape to block 1.");
            dumpLog.WriteLine("Positioning tape to block 1");

            sense = dev.Locate16(out senseBuf, 1, dev.Timeout, out _);

            if(!sense)
            {
                sense = dev.ReadPositionLong(out cmdBuf, out senseBuf, dev.Timeout, out _);

                if(!sense)
                {
                    ulong position = Swapping.Swap(BitConverter.ToUInt64(cmdBuf, 8));

                    if(position == 1)
                    {
                        canLocateLong = true;
                        UpdateStatus?.Invoke("LOCATE LONG works.");
                        dumpLog.WriteLine("LOCATE LONG works.");
                    }
                }
                else fxSense = Sense.DecodeFixed(senseBuf, out strSense);
            }
            else fxSense = Sense.DecodeFixed(senseBuf, out strSense);

            sense = dev.Locate(out senseBuf, 1, dev.Timeout, out _);

            if(!sense)
            {
                sense = dev.ReadPosition(out cmdBuf, out senseBuf, dev.Timeout, out _);

                if(!sense)
                {
                    ulong position = Swapping.Swap(BitConverter.ToUInt32(cmdBuf, 4));

                    if(position == 1)
                    {
                        canLocate = true;
                        UpdateStatus?.Invoke("LOCATE works.");
                        dumpLog.WriteLine("LOCATE works.");
                    }
                }
                else fxSense = Sense.DecodeFixed(senseBuf, out strSense);
            }
            else fxSense = Sense.DecodeFixed(senseBuf, out strSense);

            if(resume.NextBlock > 0)
            {
                UpdateStatus?.Invoke($"Positioning tape to block {resume.NextBlock}.");
                dumpLog.WriteLine("Positioning tape to block {0}.", resume.NextBlock);
                if(canLocateLong)
                {
                    sense = dev.Locate16(out senseBuf, resume.NextBlock, dev.Timeout, out _);

                    if(!sense)
                    {
                        sense = dev.ReadPositionLong(out cmdBuf, out senseBuf, dev.Timeout, out _);

                        if(sense)
                        {
                            if(!force)
                            {
                                dumpLog
                                   .WriteLine("Could not check current position, unable to resume. If you want to continue use force.");
                                StoppingErrorMessage
                                  ?.Invoke("Could not check current position, unable to resume. If you want to continue use force.");
                                return;
                            }

                            dumpLog
                               .WriteLine("Could not check current position, unable to resume. Dumping from the start.");
                            ErrorMessage
                              ?.Invoke("Could not check current position, unable to resume. Dumping from the start.");
                            canLocateLong = false;
                        }
                        else
                        {
                            ulong position = Swapping.Swap(BitConverter.ToUInt64(cmdBuf, 8));

                            if(position != resume.NextBlock)
                            {
                                if(!force)
                                {
                                    dumpLog
                                       .WriteLine("Current position is not as expected, unable to resume. If you want to continue use force.");
                                    StoppingErrorMessage
                                      ?.Invoke("Current position is not as expected, unable to resume. If you want to continue use force.");
                                    return;
                                }

                                dumpLog
                                   .WriteLine("Current position is not as expected, unable to resume. Dumping from the start.");
                                ErrorMessage
                                  ?.Invoke("Current position is not as expected, unable to resume. Dumping from the start.");
                                canLocateLong = false;
                            }
                        }
                    }
                    else
                    {
                        if(!force)
                        {
                            dumpLog
                               .WriteLine("Cannot reposition tape, unable to resume. If you want to continue use force.");
                            StoppingErrorMessage
                              ?.Invoke("Cannot reposition tape, unable to resume. If you want to continue use force.");
                            return;
                        }

                        dumpLog.WriteLine("Cannot reposition tape, unable to resume. Dumping from the start.");
                        ErrorMessage?.Invoke("Cannot reposition tape, unable to resume. Dumping from the start.");
                        canLocateLong = false;
                    }
                }
                else if(canLocate)
                {
                    sense = dev.Locate(out senseBuf, (uint)resume.NextBlock, dev.Timeout, out _);

                    if(!sense)
                    {
                        sense = dev.ReadPosition(out cmdBuf, out senseBuf, dev.Timeout, out _);

                        if(sense)
                        {
                            if(!force)
                            {
                                dumpLog
                                   .WriteLine("Could not check current position, unable to resume. If you want to continue use force.");
                                StoppingErrorMessage
                                  ?.Invoke("Could not check current position, unable to resume. If you want to continue use force.");
                                return;
                            }

                            dumpLog
                               .WriteLine("Could not check current position, unable to resume. Dumping from the start.");
                            ErrorMessage
                              ?.Invoke("Could not check current position, unable to resume. Dumping from the start.");
                            canLocate = false;
                        }
                        else
                        {
                            ulong position = Swapping.Swap(BitConverter.ToUInt32(cmdBuf, 4));

                            if(position != resume.NextBlock)
                            {
                                if(!force)
                                {
                                    dumpLog
                                       .WriteLine("Current position is not as expected, unable to resume. If you want to continue use force.");
                                    StoppingErrorMessage
                                      ?.Invoke("Current position is not as expected, unable to resume. If you want to continue use force.");
                                    return;
                                }

                                dumpLog
                                   .WriteLine("Current position is not as expected, unable to resume. Dumping from the start.");
                                ErrorMessage
                                  ?.Invoke("Current position is not as expected, unable to resume. Dumping from the start.");
                                canLocate = false;
                            }
                        }
                    }
                    else
                    {
                        if(!force)
                        {
                            dumpLog
                               .WriteLine("Cannot reposition tape, unable to resume. If you want to continue use force.");
                            StoppingErrorMessage
                              ?.Invoke("Cannot reposition tape, unable to resume. If you want to continue use force.");
                            return;
                        }

                        dumpLog.WriteLine("Cannot reposition tape, unable to resume. Dumping from the start.");
                        ErrorMessage?.Invoke("Cannot reposition tape, unable to resume. Dumping from the start.");
                        canLocate = false;
                    }
                }
                else
                {
                    if(!force)
                    {
                        dumpLog.WriteLine("Cannot reposition tape, unable to resume. If you want to continue use force.");
                        StoppingErrorMessage
                          ?.Invoke("Cannot reposition tape, unable to resume. If you want to continue use force.");
                        return;
                    }

                    dumpLog.WriteLine("Cannot reposition tape, unable to resume. Dumping from the start.");
                    ErrorMessage?.Invoke("Cannot reposition tape, unable to resume. Dumping from the start.");
                    canLocate = false;
                }
            }
            else
            {
                sense = canLocateLong
                            ? dev.Locate16(out senseBuf, false, 0, 0, dev.Timeout, out duration)
                            : dev.Locate(out senseBuf, false, 0, 0, dev.Timeout, out duration);

                do
                {
                    Thread.Sleep(1000);
                    PulseProgress?.Invoke("Rewinding, please wait...");
                    dev.RequestSense(out senseBuf, dev.Timeout, out duration);
                    fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                }
                while(fxSense.HasValue && fxSense?.ASC == 0x00 && (fxSense?.ASCQ == 0x1A || fxSense?.ASCQ == 0x19));

                // And yet, did not rewind!
                if(fxSense.HasValue && (fxSense?.ASC == 0x00 && fxSense?.ASCQ != 0x00 && fxSense?.ASCQ != 0x04 ||
                                        fxSense?.ASC != 0x00))
                {
                    StoppingErrorMessage?.Invoke("Drive could not rewind, please correct. Sense follows..." +
                                                 Environment.NewLine                                        + strSense);
                    dumpLog.WriteLine("Drive could not rewind, please correct. Sense follows...");
                    dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                      fxSense?.ASC, fxSense?.ASCQ);
                    return;
                }
            }

            bool ret = (outputPlugin as IWritableTapeImage).SetTape();
            // Cannot set image to tape mode
            if(!ret)
            {
                dumpLog.WriteLine("Error setting output image in tape mode, not continuing.");
                dumpLog.WriteLine(outputPlugin.ErrorMessage);
                StoppingErrorMessage?.Invoke("Error setting output image in tape mode, not continuing." +
                                             Environment.NewLine                                        +
                                             outputPlugin.ErrorMessage);
                return;
            }

            ret = outputPlugin.Create(outputPath, dskType, formatOptions, 0, 0);

            // Cannot create image
            if(!ret)
            {
                dumpLog.WriteLine("Error creating output image, not continuing.");
                dumpLog.WriteLine(outputPlugin.ErrorMessage);
                StoppingErrorMessage?.Invoke("Error creating output image, not continuing." + Environment.NewLine +
                                             outputPlugin.ErrorMessage);
                return;
            }

            start = DateTime.UtcNow;
            MhddLog mhddLog = new MhddLog(outputPrefix + ".mhddlog.bin", dev, blocks, blockSize, 1);
            IbgLog  ibgLog  = new IbgLog(outputPrefix  + ".ibg", 0x0008);

            TapeFile currentTapeFile =
                new TapeFile {File = currentFile, FirstBlock = currentBlock, Partition = currentPartition};
            TapePartition currentTapePartition =
                new TapePartition {Number = currentPartition, FirstBlock = currentBlock};

            if((canLocate || canLocateLong) && resume.NextBlock > 0)
            {
                currentBlock = resume.NextBlock;

                currentTapeFile =
                    (outputPlugin as IWritableTapeImage).Files.FirstOrDefault(f => f.LastBlock ==
                                                                                   (outputPlugin as IWritableTapeImage)
                                                                                 ?.Files.Max(g => g.LastBlock));

                currentTapePartition =
                    (outputPlugin as IWritableTapeImage).TapePartitions.FirstOrDefault(p => p.LastBlock ==
                                                                                            (outputPlugin as
                                                                                                 IWritableTapeImage)
                                                                                          ?.TapePartitions
                                                                                           .Max(g => g.LastBlock));
            }

            if(mode6Data  != null) outputPlugin.WriteMediaTag(mode6Data,  MediaTagType.SCSI_MODESENSE_6);
            if(mode10Data != null) outputPlugin.WriteMediaTag(mode10Data, MediaTagType.SCSI_MODESENSE_10);

            DateTime timeSpeedStart     = DateTime.UtcNow;
            ulong    currentSpeedSize   = 0;
            double   imageWriteDuration = 0;

            InitProgress?.Invoke();
            while(currentPartition < totalPartitions)
            {
                if(aborted)
                {
                    currentTry.Extents = ExtentsConverter.ToMetadata(extents);
                    UpdateStatus?.Invoke("Aborted!");
                    dumpLog.WriteLine("Aborted!");
                    break;
                }

                if(endOfMedia)
                {
                    UpdateStatus?.Invoke($"Finished partition {currentPartition}");
                    dumpLog.WriteLine("Finished partition {0}", currentPartition);

                    currentTapeFile.LastBlock = currentBlock - 1;
                    if(currentTapeFile.LastBlock > currentTapeFile.FirstBlock)
                        (outputPlugin as IWritableTapeImage).AddFile(currentTapeFile);

                    currentTapePartition.LastBlock = currentBlock - 1;
                    (outputPlugin as IWritableTapeImage).AddPartition(currentTapePartition);

                    currentPartition++;

                    if(currentPartition < totalPartitions)
                    {
                        currentFile++;
                        currentTapeFile = new TapeFile
                        {
                            File = currentFile, FirstBlock = currentBlock, Partition = currentPartition
                        };
                        currentTapePartition = new TapePartition {Number = currentPartition, FirstBlock = currentBlock};
                        UpdateStatus?.Invoke($"Seeking to partition {currentPartition}");
                        dev.Locate(out senseBuf, false, currentPartition, 0, dev.Timeout, out duration);
                        totalDuration += duration;
                    }

                    continue;
                }

                #pragma warning disable RECS0018 // Comparison of floating point numbers with equality operator
                if(currentSpeed > maxSpeed && currentSpeed != 0) maxSpeed = currentSpeed;
                if(currentSpeed < minSpeed && currentSpeed != 0) minSpeed = currentSpeed;
                #pragma warning restore RECS0018 // Comparison of floating point numbers with equality operator

                PulseProgress?.Invoke($"Reading block {currentBlock} ({currentSpeed:F3} MiB/sec.)");

                sense = dev.Read6(out cmdBuf, out senseBuf, false, fixedLen, transferLen, blockSize, dev.Timeout,
                                  out duration);
                totalDuration += duration;

                if(sense && senseBuf?.Length != 0 && !ArrayHelpers.ArrayIsNullOrEmpty(senseBuf))
                {
                    fxSense = Sense.DecodeFixed(senseBuf, out strSense);

                    if(fxSense?.ASC              == 0x00 && fxSense?.ASCQ == 0x00 && fxSense?.ILI == true &&
                       fxSense?.InformationValid == true)
                    {
                        blockSize = (uint)((int)blockSize -
                                           BitConverter.ToInt32(BitConverter.GetBytes(fxSense.Value.Information), 0));
                        if(!fixedLen) transferLen = blockSize;

                        UpdateStatus?.Invoke($"Blocksize changed to {blockSize} bytes at block {currentBlock}");
                        dumpLog.WriteLine("Blocksize changed to {0} bytes at block {1}", blockSize, currentBlock);

                        sense = dev.Space(out senseBuf, SscSpaceCodes.LogicalBlock, -1, dev.Timeout,
                                                   out duration);
                        totalDuration += duration;

                        if(sense)
                        {
                            fxSense = Sense.DecodeFixed(senseBuf, out strSense);
                            StoppingErrorMessage?.Invoke("Drive could not go back one block. Sense follows..." +
                                                         Environment.NewLine                                   +
                                                         strSense);
                            outputPlugin.Close();
                            dumpLog.WriteLine("Drive could not go back one block. Sense follows...");
                            dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h",
                                              fxSense?.SenseKey, fxSense?.ASC, fxSense?.ASCQ);
                            return;
                        }

                        continue;
                    }

                    switch(fxSense?.SenseKey)
                    {
                        case SenseKeys.BlankCheck when currentBlock == 0:
                            StoppingErrorMessage?.Invoke("Cannot dump a blank tape...");
                            outputPlugin.Close();
                            dumpLog.WriteLine("Cannot dump a blank tape...");
                            return;
                        // For sure this is an end-of-tape/partition
                        case SenseKeys.BlankCheck when fxSense?.ASC == 0x00 &&
                                                       (fxSense?.ASCQ == 0x02 || fxSense?.ASCQ == 0x05 ||
                                                        fxSense?.EOM  == true):
                            // TODO: Detect end of partition
                            endOfMedia = true;
                            UpdateStatus?.Invoke("Found end-of-tape/partition...");
                            dumpLog.WriteLine("Found end-of-tape/partition...");
                            continue;
                        case SenseKeys.BlankCheck:
                            StoppingErrorMessage?.Invoke("Blank block found, end of tape?...");
                            endOfMedia = true;
                            dumpLog.WriteLine("Blank block found, end of tape?...");
                            continue;
                    }

                    if((fxSense?.SenseKey == SenseKeys.NoSense ||
                        fxSense?.SenseKey == SenseKeys.RecoveredError) &&
                       (fxSense?.ASCQ == 0x02 || fxSense?.ASCQ == 0x05 || fxSense?.EOM == true))
                    {
                        // TODO: Detect end of partition
                        endOfMedia = true;
                        UpdateStatus?.Invoke("Found end-of-tape/partition...");
                        dumpLog.WriteLine("Found end-of-tape/partition...");
                        continue;
                    }

                    if((fxSense?.SenseKey == SenseKeys.NoSense || fxSense?.SenseKey == SenseKeys.RecoveredError) &&
                       (fxSense?.ASCQ     == 0x01              || fxSense?.Filemark == true))
                    {
                        currentTapeFile.LastBlock = currentBlock - 1;
                        (outputPlugin as IWritableTapeImage).AddFile(currentTapeFile);

                        currentFile++;
                        currentTapeFile = new TapeFile
                        {
                            File = currentFile, FirstBlock = currentBlock, Partition = currentPartition
                        };

                        UpdateStatus?.Invoke($"Changed to file {currentFile} at block {currentBlock}");
                        dumpLog.WriteLine("Changed to file {0} at block {1}", currentFile, currentBlock);
                        continue;
                    }

                    if(fxSense is null)
                    {
                        StoppingErrorMessage
                          ?.Invoke($"Drive could not read block ${currentBlock}. Sense cannot be decoded, look at log for dump...");
                        dumpLog.WriteLine($"Drive could not read block ${currentBlock}. Sense bytes follow...");
                        dumpLog.WriteLine(PrintHex.ByteArrayToHexArrayString(senseBuf, 32));
                    }
                    else
                    {
                        StoppingErrorMessage
                          ?.Invoke($"Drive could not read block ${currentBlock}. Sense follows...\n{fxSense?.SenseKey} {strSense}");
                        dumpLog.WriteLine($"Drive could not read block ${currentBlock}. Sense follows...");
                        dumpLog.WriteLine("Device not ready. Sense {0}h ASC {1:X2}h ASCQ {2:X2}h", fxSense?.SenseKey,
                                          fxSense?.ASC, fxSense?.ASCQ);
                    }

                    // TODO: Reset device after X errors
                    if(stopOnError) return; // TODO: Return more cleanly

                    // Write empty data
                    DateTime writeStart = DateTime.Now;
                    outputPlugin.WriteSector(new byte[blockSize], currentBlock);
                    imageWriteDuration += (DateTime.Now - writeStart).TotalSeconds;

                    mhddLog.Write(currentBlock, duration < 500 ? 65535 : duration);
                    ibgLog.Write(currentBlock, 0);
                    resume.BadBlocks.Add(currentBlock);
                }
                else
                {
                    mhddLog.Write(currentBlock, duration);
                    ibgLog.Write(currentBlock, currentSpeed * 1024);
                    DateTime writeStart = DateTime.Now;
                    outputPlugin.WriteSector(cmdBuf, currentBlock);
                    imageWriteDuration += (DateTime.Now - writeStart).TotalSeconds;
                    extents.Add(currentBlock, 1, true);
                }

                currentBlock++;
                resume.NextBlock++;
                currentSpeedSize += blockSize;

                double elapsed = (DateTime.UtcNow - timeSpeedStart).TotalSeconds;
                if(elapsed < 1) continue;

                currentSpeed     = currentSpeedSize / (1048576 * elapsed);
                currentSpeedSize = 0;
                timeSpeedStart   = DateTime.UtcNow;
            }

            blocks = currentBlock + 1;
            end    = DateTime.UtcNow;

            // If not aborted this is added at the end of medium
            if(aborted)
            {
                currentTapeFile.LastBlock = currentBlock - 1;
                (outputPlugin as IWritableTapeImage).AddFile(currentTapeFile);

                currentTapePartition.LastBlock = currentBlock - 1;
                (outputPlugin as IWritableTapeImage).AddPartition(currentTapePartition);
            }

            EndProgress?.Invoke();
            mhddLog.Close();
            ibgLog.Close(dev, blocks, blockSize, (end - start).TotalSeconds, currentSpeed * 1024,
                         blockSize * (double)(blocks + 1) / 1024                          / (totalDuration / 1000),
                         devicePath);
            UpdateStatus?.Invoke($"Dump finished in {(end - start).TotalSeconds} seconds.");
            UpdateStatus
              ?.Invoke($"Average dump speed {(double)blockSize * (double)(blocks + 1) / 1024 / (totalDuration / 1000):F3} KiB/sec.");
            UpdateStatus
              ?.Invoke($"Average write speed {(double)blockSize * (double)(blocks + 1) / 1024 / imageWriteDuration:F3} KiB/sec.");
            dumpLog.WriteLine("Dump finished in {0} seconds.", (end - start).TotalSeconds);
            dumpLog.WriteLine("Average dump speed {0:F3} KiB/sec.",
                              (double)blockSize * (double)(blocks + 1) / 1024 / (totalDuration / 1000));
            dumpLog.WriteLine("Average write speed {0:F3} KiB/sec.",
                              (double)blockSize * (double)(blocks + 1) / 1024 / imageWriteDuration);

            #region Error handling
            if(resume.BadBlocks.Count > 0 && !aborted && retryPasses > 0 && (canLocate || canLocateLong))
            {
                int  pass              = 1;
                bool forward           = false;
                bool runningPersistent = false;

                Modes.ModePage? currentModePage = null;
                byte[]          md6;
                byte[]          md10;

                if(persistent)
                {
                    // TODO: Implement persistent
                }

                InitProgress?.Invoke();
                repeatRetry:
                ulong[] tmpArray = resume.BadBlocks.ToArray();
                foreach(ulong badBlock in tmpArray)
                {
                    if(aborted)
                    {
                        currentTry.Extents = ExtentsConverter.ToMetadata(extents);
                        UpdateStatus?.Invoke("Aborted!");
                        dumpLog.WriteLine("Aborted!");
                        break;
                    }

                    PulseProgress?.Invoke(string.Format("Retrying block {0}, pass {1}, {3}{2}", badBlock, pass,
                                                        forward ? "forward" : "reverse",
                                                        runningPersistent ? "recovering partial data, " : ""));

                    UpdateStatus?.Invoke($"Positioning tape to block {badBlock}.");
                    dumpLog.WriteLine($"Positioning tape to block {badBlock}.");
                    if(canLocateLong)
                    {
                        sense = dev.Locate16(out senseBuf, resume.NextBlock, dev.Timeout, out _);

                        if(!sense)
                        {
                            sense = dev.ReadPositionLong(out cmdBuf, out senseBuf, dev.Timeout, out _);

                            if(sense)
                            {
                                dumpLog.WriteLine("Could not check current position, continuing.");
                                StoppingErrorMessage?.Invoke("Could not check current position, continuing.");
                                continue;
                            }

                            ulong position = Swapping.Swap(BitConverter.ToUInt64(cmdBuf, 8));

                            if(position != resume.NextBlock)
                            {
                                dumpLog.WriteLine("Current position is not as expected, continuing.");
                                StoppingErrorMessage?.Invoke("Current position is not as expected, continuing.");
                                continue;
                            }
                        }
                        else
                        {
                            dumpLog.WriteLine($"Cannot position tape to block {badBlock}.");
                            ErrorMessage?.Invoke($"Cannot position tape to block {badBlock}.");
                            continue;
                        }
                    }
                    else
                    {
                        sense = dev.Locate(out senseBuf, (uint)resume.NextBlock, dev.Timeout, out _);

                        if(!sense)
                        {
                            sense = dev.ReadPosition(out cmdBuf, out senseBuf, dev.Timeout, out _);

                            if(sense)
                            {
                                dumpLog.WriteLine("Could not check current position, continuing.");
                                StoppingErrorMessage?.Invoke("Could not check current position, continuing.");
                                continue;
                            }

                            ulong position = Swapping.Swap(BitConverter.ToUInt32(cmdBuf, 4));

                            if(position != resume.NextBlock)
                            {
                                dumpLog.WriteLine("Current position is not as expected, continuing.");
                                StoppingErrorMessage?.Invoke("Current position is not as expected, continuing.");
                                continue;
                            }
                        }
                        else
                        {
                            dumpLog.WriteLine($"Cannot position tape to block {badBlock}.");
                            ErrorMessage?.Invoke($"Cannot position tape to block {badBlock}.");
                            continue;
                        }
                    }

                    sense = dev.Read6(out cmdBuf, out senseBuf, false, fixedLen, transferLen, blockSize, dev.Timeout,
                                      out duration);
                    totalDuration += duration;

                    if(!sense && !dev.Error)
                    {
                        resume.BadBlocks.Remove(badBlock);
                        extents.Add(badBlock);
                        outputPlugin.WriteSector(cmdBuf, badBlock);
                        UpdateStatus?.Invoke($"Correctly retried block {badBlock} in pass {pass}.");
                        dumpLog.WriteLine("Correctly retried block {0} in pass {1}.", badBlock, pass);
                    }
                    else if(runningPersistent) outputPlugin.WriteSector(cmdBuf, badBlock);
                }

                if(pass < retryPasses && !aborted && resume.BadBlocks.Count > 0)
                {
                    pass++;
                    forward = !forward;
                    resume.BadBlocks.Sort();
                    resume.BadBlocks.Reverse();
                    goto repeatRetry;
                }

                if(runningPersistent && currentModePage.HasValue)
                {
                    // TODO: Persistent mode
                }

                EndProgress?.Invoke();
            }
            #endregion Error handling

            resume.BadBlocks.Sort();
            foreach(ulong bad in resume.BadBlocks) dumpLog.WriteLine("Block {0} could not be read.", bad);
            currentTry.Extents = ExtentsConverter.ToMetadata(extents);

            outputPlugin.SetDumpHardware(resume.Tries);
            if(preSidecar != null) outputPlugin.SetCicmMetadata(preSidecar);
            dumpLog.WriteLine("Closing output file.");
            UpdateStatus?.Invoke("Closing output file.");
            DateTime closeStart = DateTime.Now;
            outputPlugin.Close();
            DateTime closeEnd = DateTime.Now;
            UpdateStatus?.Invoke($"Closed in {(closeEnd - closeStart).TotalSeconds} seconds.");
            dumpLog.WriteLine("Closed in {0} seconds.", (closeEnd - closeStart).TotalSeconds);

            if(aborted)
            {
                UpdateStatus?.Invoke("Aborted!");
                dumpLog.WriteLine("Aborted!");
                return;
            }

            if(aborted)
            {
                UpdateStatus?.Invoke("Aborted!");
                dumpLog.WriteLine("Aborted!");
                return;
            }

            double totalChkDuration = 0;
            if(!nometadata)
            {
                UpdateStatus?.Invoke("Creating sidecar.");
                dumpLog.WriteLine("Creating sidecar.");
                FiltersList filters     = new FiltersList();
                IFilter     filter      = filters.GetFilter(outputPath);
                IMediaImage inputPlugin = ImageFormat.Detect(filter);
                if(!inputPlugin.Open(filter))
                {
                    StoppingErrorMessage?.Invoke("Could not open created image.");
                    return;
                }

                DateTime chkStart = DateTime.UtcNow;
                sidecarClass                      =  new Sidecar(inputPlugin, outputPath, filter.Id, encoding);
                sidecarClass.InitProgressEvent    += InitProgress;
                sidecarClass.UpdateProgressEvent  += UpdateProgress;
                sidecarClass.EndProgressEvent     += EndProgress;
                sidecarClass.InitProgressEvent2   += InitProgress2;
                sidecarClass.UpdateProgressEvent2 += UpdateProgress2;
                sidecarClass.EndProgressEvent2    += EndProgress2;
                sidecarClass.UpdateStatusEvent    += UpdateStatus;
                CICMMetadataType sidecar = sidecarClass.Create();
                end = DateTime.UtcNow;

                totalChkDuration = (end - chkStart).TotalMilliseconds;
                UpdateStatus?.Invoke($"Sidecar created in {(end - chkStart).TotalSeconds} seconds.");
                UpdateStatus
                  ?.Invoke($"Average checksum speed {(double)blockSize * (double)(blocks + 1) / 1024 / (totalChkDuration / 1000):F3} KiB/sec.");
                dumpLog.WriteLine("Sidecar created in {0} seconds.", (end - chkStart).TotalSeconds);
                dumpLog.WriteLine("Average checksum speed {0:F3} KiB/sec.",
                                  (double)blockSize * (double)(blocks + 1) / 1024 / (totalChkDuration / 1000));

                if(preSidecar != null)
                {
                    preSidecar.BlockMedia = sidecar.BlockMedia;
                    sidecar               = preSidecar;
                }

                List<(ulong start, string type)> filesystems = new List<(ulong start, string type)>();
                if(sidecar.BlockMedia[0].FileSystemInformation != null)
                    filesystems.AddRange(from partition in sidecar.BlockMedia[0].FileSystemInformation
                                         where partition.FileSystems != null
                                         from fileSystem in partition.FileSystems
                                         select (partition.StartSector, fileSystem.Type));

                if(filesystems.Count > 0)
                    foreach(var filesystem in filesystems.Select(o => new {o.start, o.type}).Distinct())
                    {
                        UpdateStatus?.Invoke($"Found filesystem {filesystem.type} at sector {filesystem.start}");
                        dumpLog.WriteLine("Found filesystem {0} at sector {1}", filesystem.type, filesystem.start);
                    }

                sidecar.BlockMedia[0].Dimensions = Dimensions.DimensionsFromMediaType(dskType);
                CommonTypes.Metadata.MediaType.MediaTypeToString(dskType, out string xmlDskTyp,
                                                                 out string xmlDskSubTyp);
                sidecar.BlockMedia[0].DiskType    = xmlDskTyp;
                sidecar.BlockMedia[0].DiskSubType = xmlDskSubTyp;
                // TODO: Implement device firmware revision
                if(!dev.IsRemovable || dev.IsUsb)
                    if(dev.Type == DeviceType.ATAPI) sidecar.BlockMedia[0].Interface = "ATAPI";
                    else if(dev.IsUsb) sidecar.BlockMedia[0].Interface               = "USB";
                    else if(dev.IsFireWire) sidecar.BlockMedia[0].Interface          = "FireWire";
                    else sidecar.BlockMedia[0].Interface                             = "SCSI";
                sidecar.BlockMedia[0].LogicalBlocks = blocks;
                sidecar.BlockMedia[0].Manufacturer  = dev.Manufacturer;
                sidecar.BlockMedia[0].Model         = dev.Model;
                sidecar.BlockMedia[0].Serial        = dev.Serial;
                sidecar.BlockMedia[0].Size          = blocks * blockSize;

                if(dev.IsRemovable) sidecar.BlockMedia[0].DumpHardwareArray = resume.Tries.ToArray();

                UpdateStatus?.Invoke("Writing metadata sidecar");

                FileStream xmlFs = new FileStream(outputPrefix + ".cicm.xml", FileMode.Create);

                XmlSerializer xmlSer = new XmlSerializer(typeof(CICMMetadataType));
                xmlSer.Serialize(xmlFs, sidecar);
                xmlFs.Close();
            }

            UpdateStatus?.Invoke("");
            UpdateStatus
              ?.Invoke($"Took a total of {(end - start).TotalSeconds:F3} seconds ({totalDuration / 1000:F3} processing commands, {totalChkDuration / 1000:F3} checksumming, {imageWriteDuration:F3} writing, {(closeEnd - closeStart).TotalSeconds:F3} closing).");
            UpdateStatus
              ?.Invoke($"Average speed: {(double)blockSize * (double)(blocks + 1) / 1048576 / (totalDuration / 1000):F3} MiB/sec.");
            UpdateStatus?.Invoke($"Fastest speed burst: {maxSpeed:F3} MiB/sec.");
            UpdateStatus?.Invoke($"Slowest speed burst: {minSpeed:F3} MiB/sec.");
            UpdateStatus?.Invoke($"{resume.BadBlocks.Count} sectors could not be read.");
            UpdateStatus?.Invoke("");

            Statistics.AddMedia(dskType, true);
        }
    }
}