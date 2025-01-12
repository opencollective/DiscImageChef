﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : ATA.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Core algorithms.
//
// --[ Description ] ----------------------------------------------------------
//
//     Creates reports from ATA devices.
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
using DiscImageChef.CommonTypes.Metadata;
using DiscImageChef.Console;
using DiscImageChef.Decoders.ATA;
using DiscImageChef.Devices;

namespace DiscImageChef.Core.Devices.Report
{
    public partial class DeviceReport
    {
        public TestedMedia ReportAtaMedia()
        {
            TestedMedia mediaTest = new TestedMedia();
            DicConsole.Write("Please write a description of the media type and press enter: ");
            mediaTest.MediumTypeName = System.Console.ReadLine();
            DicConsole.Write("Please write the media model and press enter: ");
            mediaTest.Model = System.Console.ReadLine();

            mediaTest.MediaIsRecognized = true;

            DicConsole.WriteLine("Querying ATA IDENTIFY...");
            dev.AtaIdentify(out byte[] buffer, out _, dev.Timeout, out _);

            mediaTest.IdentifyData   = ClearIdentify(buffer);
            mediaTest.IdentifyDevice = Identify.Decode(buffer);

            if(mediaTest.IdentifyDevice.HasValue)
            {
                Identify.IdentifyDevice ataId = mediaTest.IdentifyDevice.Value;

                if(ataId.UnformattedBPT != 0) mediaTest.UnformattedBPT = ataId.UnformattedBPT;

                if(ataId.UnformattedBPS != 0) mediaTest.UnformattedBPS = ataId.UnformattedBPS;

                if(ataId.Cylinders > 0 && ataId.Heads > 0 && ataId.SectorsPerTrack > 0)
                {
                    mediaTest.CHS = new Chs
                    {
                        Cylinders = ataId.Cylinders, Heads = ataId.Heads, Sectors = ataId.SectorsPerTrack
                    };
                    mediaTest.Blocks = (ulong)(ataId.Cylinders * ataId.Heads * ataId.SectorsPerTrack);
                }

                if(ataId.CurrentCylinders > 0 && ataId.CurrentHeads > 0 && ataId.CurrentSectorsPerTrack > 0)
                {
                    mediaTest.CurrentCHS = new Chs
                    {
                        Cylinders = ataId.CurrentCylinders,
                        Heads     = ataId.CurrentHeads,
                        Sectors   = ataId.CurrentSectorsPerTrack
                    };
                    if(mediaTest.Blocks == 0)
                        mediaTest.Blocks =
                            (ulong)(ataId.CurrentCylinders * ataId.CurrentHeads * ataId.CurrentSectorsPerTrack);
                }

                if(ataId.Capabilities.HasFlag(Identify.CapabilitiesBit.LBASupport))
                {
                    mediaTest.LBASectors = ataId.LBASectors;
                    mediaTest.Blocks     = ataId.LBASectors;
                }

                if(ataId.CommandSet2.HasFlag(Identify.CommandSetBit2.LBA48))
                {
                    mediaTest.LBA48Sectors = ataId.LBA48Sectors;
                    mediaTest.Blocks       = ataId.LBA48Sectors;
                }

                if(ataId.NominalRotationRate != 0x0000 && ataId.NominalRotationRate != 0xFFFF)
                    if(ataId.NominalRotationRate == 0x0001)
                        mediaTest.SolidStateDevice = true;
                    else
                    {
                        mediaTest.SolidStateDevice    = false;
                        mediaTest.NominalRotationRate = ataId.NominalRotationRate;
                    }

                uint logicalsectorsize;
                uint physicalsectorsize;
                if((ataId.PhysLogSectorSize & 0x8000) == 0x0000 && (ataId.PhysLogSectorSize & 0x4000) == 0x4000)
                {
                    if((ataId.PhysLogSectorSize & 0x1000) == 0x1000)
                        if(ataId.LogicalSectorWords <= 255 || ataId.LogicalAlignment == 0xFFFF)
                            logicalsectorsize = 512;
                        else
                            logicalsectorsize = ataId.LogicalSectorWords * 2;
                    else logicalsectorsize = 512;

                    if((ataId.PhysLogSectorSize & 0x2000) == 0x2000)
                        physicalsectorsize  = (uint)(logicalsectorsize * ((1 << ataId.PhysLogSectorSize) & 0xF));
                    else physicalsectorsize = logicalsectorsize;
                }
                else
                {
                    logicalsectorsize  = 512;
                    physicalsectorsize = 512;
                }

                mediaTest.BlockSize = logicalsectorsize;
                if(physicalsectorsize != logicalsectorsize)
                {
                    mediaTest.PhysicalBlockSize = physicalsectorsize;

                    if((ataId.LogicalAlignment & 0x8000) == 0x0000 && (ataId.LogicalAlignment & 0x4000) == 0x4000)
                        mediaTest.LogicalAlignment = (ushort)(ataId.LogicalAlignment & 0x3FFF);
                }

                if(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF)
                    mediaTest.LongBlockSize = logicalsectorsize + ataId.EccBytes;

                if(ataId.UnformattedBPS > logicalsectorsize &&
                   (!(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF) || mediaTest.LongBlockSize == 516))
                    mediaTest.LongBlockSize = ataId.UnformattedBPS;

                if(ataId.CommandSet3.HasFlag(Identify.CommandSetBit3.MustBeSet)    &&
                   !ataId.CommandSet3.HasFlag(Identify.CommandSetBit3.MustBeClear) &&
                   ataId.EnabledCommandSet3.HasFlag(Identify.CommandSetBit3.MediaSerial))
                {
                    mediaTest.CanReadMediaSerial = true;
                    if(!string.IsNullOrWhiteSpace(ataId.MediaManufacturer))
                        mediaTest.Manufacturer = ataId.MediaManufacturer;
                }

                ulong checkCorrectRead = BitConverter.ToUInt64(buffer, 0);
                bool  sense;

                DicConsole.WriteLine("Trying READ SECTOR(S) in CHS mode...");
                sense = dev.Read(out byte[] readBuf, out AtaErrorRegistersChs errorChs, false, 0, 0, 1, 1, dev.Timeout,
                                 out _);
                mediaTest.SupportsReadSectors = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                                readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadSectorsData = readBuf;

                DicConsole.WriteLine("Trying READ SECTOR(S) RETRY in CHS mode...");
                sense = dev.Read(out readBuf, out errorChs, true, 0, 0, 1, 1, dev.Timeout, out _);
                mediaTest.SupportsReadRetry = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                              readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadSectorsRetryData = readBuf;

                DicConsole.WriteLine("Trying READ DMA in CHS mode...");
                sense = dev.ReadDma(out readBuf, out errorChs, false, 0, 0, 1, 1, dev.Timeout, out _);
                mediaTest.SupportsReadDma = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                            readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadDmaData = readBuf;

                DicConsole.WriteLine("Trying READ DMA RETRY in CHS mode...");
                sense = dev.ReadDma(out readBuf, out errorChs, true, 0, 0, 1, 1, dev.Timeout, out _);
                mediaTest.SupportsReadDmaRetry = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                                 readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadDmaRetryData = readBuf;

                DicConsole.WriteLine("Trying SEEK in CHS mode...");
                sense                  = dev.Seek(out errorChs, 0, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsSeek = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0;
                DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}", sense,
                                          errorChs.Status, errorChs.Error);

                DicConsole.WriteLine("Trying READ SECTOR(S) in LBA mode...");
                sense = dev.Read(out readBuf, out AtaErrorRegistersLba28 errorLba, false, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsReadLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                            readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadLbaData = readBuf;

                DicConsole.WriteLine("Trying READ SECTOR(S) RETRY in LBA mode...");
                sense = dev.Read(out readBuf, out errorLba, true, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsReadRetryLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                                 readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadRetryLbaData = readBuf;

                DicConsole.WriteLine("Trying READ DMA in LBA mode...");
                sense = dev.ReadDma(out readBuf, out errorLba, false, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsReadDmaLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                               readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadDmaLbaData = readBuf;

                DicConsole.WriteLine("Trying READ DMA RETRY in LBA mode...");
                sense = dev.ReadDma(out readBuf, out errorLba, true, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsReadDmaRetryLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                                    readBuf.Length                     > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadDmaRetryLbaData = readBuf;

                DicConsole.WriteLine("Trying SEEK in LBA mode...");
                sense                     = dev.Seek(out errorLba, 0, dev.Timeout, out _);
                mediaTest.SupportsSeekLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0;
                DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}", sense,
                                          errorChs.Status, errorChs.Error);

                DicConsole.WriteLine("Trying READ SECTOR(S) in LBA48 mode...");
                sense = dev.Read(out readBuf, out AtaErrorRegistersLba48 errorLba48, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsReadLba48 = !sense && (errorLba48.Status & 0x01) != 0x01 && errorLba48.Error == 0 &&
                                              readBuf.Length                       > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadLba48Data = readBuf;

                DicConsole.WriteLine("Trying READ DMA in LBA48 mode...");
                sense = dev.ReadDma(out readBuf, out errorLba48, 0, 1, dev.Timeout, out _);
                mediaTest.SupportsReadDmaLba48 = !sense && (errorLba48.Status & 0x01) != 0x01 &&
                                                 errorLba48.Error                     == 0    && readBuf.Length > 0;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadDmaLba48Data = readBuf;

                // Send SET FEATURES before sending READ LONG commands, retrieve IDENTIFY again and
                // check if ECC size changed. Sector is set to 1 because without it most drives just return
                // CORRECTABLE ERROR for this command.
                dev.SetFeatures(out _, AtaFeatures.EnableReadLongVendorLength, 0, 0, 1, 0, dev.Timeout, out _);

                dev.AtaIdentify(out buffer, out _, dev.Timeout, out _);
                if(Identify.Decode(buffer).HasValue)
                {
                    ataId = Identify.Decode(buffer).Value;
                    if(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF)
                        mediaTest.LongBlockSize = logicalsectorsize + ataId.EccBytes;

                    if(ataId.UnformattedBPS > logicalsectorsize &&
                       (!(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF) || mediaTest.LongBlockSize == 516))
                        mediaTest.LongBlockSize = ataId.UnformattedBPS;
                }

                DicConsole.WriteLine("Trying READ LONG in CHS mode...");
                sense = dev.ReadLong(out readBuf, out errorChs, false, 0, 0, 1, mediaTest.LongBlockSize ?? 0,
                                     dev.Timeout, out _);
                mediaTest.SupportsReadLong = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                             readBuf.Length                     > 0     &&
                                             BitConverter.ToUInt64(readBuf, 0)  != checkCorrectRead;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadLongData = readBuf;

                DicConsole.WriteLine("Trying READ LONG RETRY in CHS mode...");
                sense = dev.ReadLong(out readBuf, out errorChs, true, 0, 0, 1, mediaTest.LongBlockSize ?? 0,
                                     dev.Timeout, out _);
                mediaTest.SupportsReadLongRetry = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                                  readBuf.Length                     > 0     &&
                                                  BitConverter.ToUInt64(readBuf, 0) !=
                                                  checkCorrectRead;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadLongRetryData = readBuf;

                DicConsole.WriteLine("Trying READ LONG in LBA mode...");
                sense = dev.ReadLong(out readBuf, out errorLba, false, 0, mediaTest.LongBlockSize ?? 0, dev.Timeout,
                                     out _);
                mediaTest.SupportsReadLongLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                                readBuf.Length                     > 0     &&
                                                BitConverter.ToUInt64(readBuf, 0)  != checkCorrectRead;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadLongLbaData = readBuf;

                DicConsole.WriteLine("Trying READ LONG RETRY in LBA mode...");
                sense = dev.ReadLong(out readBuf, out errorLba, true, 0, mediaTest.LongBlockSize ?? 0, dev.Timeout,
                                     out _);
                mediaTest.SupportsReadLongRetryLba = !sense && (errorLba.Status & 0x01) != 0x01 &&
                                                     errorLba.Error                     == 0    && readBuf.Length > 0 &&
                                                     BitConverter.ToUInt64(readBuf, 0)  != checkCorrectRead;
                DicConsole.DebugWriteLine("ATA Report",
                                          "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}", sense,
                                          errorChs.Status, errorChs.Error, readBuf.Length);
                if(debug) mediaTest.ReadLongRetryLbaData = readBuf;
            }
            else mediaTest.MediaIsRecognized = false;

            return mediaTest;
        }

        /// <summary>
        ///     Creates a report of an ATA device
        /// </summary>
        public TestedMedia ReportAta(Identify.IdentifyDevice ataId)
        {
            TestedMedia capabilities = new TestedMedia();

            if(ataId.UnformattedBPT != 0) capabilities.UnformattedBPT = ataId.UnformattedBPT;

            if(ataId.UnformattedBPS != 0) capabilities.UnformattedBPS = ataId.UnformattedBPS;

            if(ataId.Cylinders > 0 && ataId.Heads > 0 && ataId.SectorsPerTrack > 0)
            {
                capabilities.CHS = new Chs
                {
                    Cylinders = ataId.Cylinders, Heads = ataId.Heads, Sectors = ataId.SectorsPerTrack
                };
                capabilities.Blocks = (ulong)(ataId.Cylinders * ataId.Heads * ataId.SectorsPerTrack);
            }

            if(ataId.CurrentCylinders > 0 && ataId.CurrentHeads > 0 && ataId.CurrentSectorsPerTrack > 0)
            {
                capabilities.CurrentCHS = new Chs
                {
                    Cylinders = ataId.CurrentCylinders,
                    Heads     = ataId.CurrentHeads,
                    Sectors   = ataId.CurrentSectorsPerTrack
                };
                capabilities.Blocks =
                    (ulong)(ataId.CurrentCylinders * ataId.CurrentHeads * ataId.CurrentSectorsPerTrack);
            }

            if(ataId.Capabilities.HasFlag(Identify.CapabilitiesBit.LBASupport))
            {
                capabilities.LBASectors = ataId.LBASectors;
                capabilities.Blocks     = ataId.LBASectors;
            }

            if(ataId.CommandSet2.HasFlag(Identify.CommandSetBit2.LBA48))
            {
                capabilities.LBA48Sectors = ataId.LBA48Sectors;
                capabilities.Blocks       = ataId.LBA48Sectors;
            }

            if(ataId.NominalRotationRate != 0x0000 && ataId.NominalRotationRate != 0xFFFF)
                if(ataId.NominalRotationRate == 0x0001)
                    capabilities.SolidStateDevice = true;
                else
                {
                    capabilities.SolidStateDevice    = false;
                    capabilities.NominalRotationRate = ataId.NominalRotationRate;
                }

            uint logicalsectorsize;
            uint physicalsectorsize;
            if((ataId.PhysLogSectorSize & 0x8000) == 0x0000 && (ataId.PhysLogSectorSize & 0x4000) == 0x4000)
            {
                if((ataId.PhysLogSectorSize & 0x1000) == 0x1000)
                    if(ataId.LogicalSectorWords <= 255 || ataId.LogicalAlignment == 0xFFFF)
                        logicalsectorsize = 512;
                    else
                        logicalsectorsize = ataId.LogicalSectorWords * 2;
                else logicalsectorsize = 512;

                if((ataId.PhysLogSectorSize & 0x2000) == 0x2000)
                    physicalsectorsize  = logicalsectorsize * (uint)Math.Pow(2, ataId.PhysLogSectorSize & 0xF);
                else physicalsectorsize = logicalsectorsize;
            }
            else
            {
                logicalsectorsize  = 512;
                physicalsectorsize = 512;
            }

            capabilities.BlockSize = logicalsectorsize;
            if(physicalsectorsize != logicalsectorsize)
            {
                capabilities.PhysicalBlockSize = physicalsectorsize;

                if((ataId.LogicalAlignment & 0x8000) == 0x0000 && (ataId.LogicalAlignment & 0x4000) == 0x4000)
                    capabilities.LogicalAlignment = (ushort)(ataId.LogicalAlignment & 0x3FFF);
            }

            if(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF)
                capabilities.LongBlockSize = logicalsectorsize + ataId.EccBytes;

            if(ataId.UnformattedBPS > logicalsectorsize &&
               (!(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF) || capabilities.LongBlockSize == 516))
                capabilities.LongBlockSize = ataId.UnformattedBPS;

            if(ataId.CommandSet3.HasFlag(Identify.CommandSetBit3.MustBeSet)    &&
               !ataId.CommandSet3.HasFlag(Identify.CommandSetBit3.MustBeClear) &&
               ataId.EnabledCommandSet3.HasFlag(Identify.CommandSetBit3.MediaSerial))
            {
                capabilities.CanReadMediaSerial = true;
                if(!string.IsNullOrWhiteSpace(ataId.MediaManufacturer))
                    capabilities.Manufacturer = ataId.MediaManufacturer;
            }

            ulong checkCorrectRead = 0;
            bool  sense;

            DicConsole.WriteLine("Trying READ SECTOR(S) in CHS mode...");
            sense = dev.Read(out byte[] readBuf, out AtaErrorRegistersChs errorChs, false, 0, 0, 1, 1, dev.Timeout,
                             out _);
            capabilities.SupportsReadSectors = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                               readBuf.Length                     > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorChs.Status, errorChs.Error, readBuf.Length);
            if(debug) capabilities.ReadSectorsData = readBuf;

            DicConsole.WriteLine("Trying READ SECTOR(S) RETRY in CHS mode...");
            sense = dev.Read(out readBuf, out errorChs, true, 0, 0, 1, 1, dev.Timeout, out _);
            capabilities.SupportsReadRetry =
                !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 && readBuf.Length > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorChs.Status, errorChs.Error, readBuf.Length);
            if(debug) capabilities.ReadSectorsRetryData = readBuf;

            DicConsole.WriteLine("Trying READ DMA in CHS mode...");
            sense = dev.ReadDma(out readBuf, out errorChs, false, 0, 0, 1, 1, dev.Timeout, out _);
            capabilities.SupportsReadDma =
                !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 && readBuf.Length > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorChs.Status, errorChs.Error, readBuf.Length);
            if(debug) capabilities.ReadDmaData = readBuf;

            DicConsole.WriteLine("Trying READ DMA RETRY in CHS mode...");
            sense = dev.ReadDma(out readBuf, out errorChs, true, 0, 0, 1, 1, dev.Timeout, out _);
            capabilities.SupportsReadDmaRetry = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                                readBuf.Length                     > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorChs.Status, errorChs.Error, readBuf.Length);
            if(debug) capabilities.ReadDmaRetryData = readBuf;

            DicConsole.WriteLine("Trying SEEK in CHS mode...");
            sense                     = dev.Seek(out errorChs, 0, 0, 1, dev.Timeout, out _);
            capabilities.SupportsSeek = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}", sense,
                                      errorChs.Status, errorChs.Error);

            DicConsole.WriteLine("Trying READ SECTOR(S) in LBA mode...");
            sense = dev.Read(out readBuf, out AtaErrorRegistersLba28 errorLba, false, 0, 1, dev.Timeout, out _);
            capabilities.SupportsReadLba =
                !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 && readBuf.Length > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba.Status, errorLba.Error, readBuf.Length);
            if(debug) capabilities.ReadLbaData = readBuf;

            DicConsole.WriteLine("Trying READ SECTOR(S) RETRY in LBA mode...");
            sense = dev.Read(out readBuf, out errorLba, true, 0, 1, dev.Timeout, out _);
            capabilities.SupportsReadRetryLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                                readBuf.Length                     > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba.Status, errorLba.Error, readBuf.Length);
            if(debug) capabilities.ReadRetryLbaData = readBuf;

            DicConsole.WriteLine("Trying READ DMA in LBA mode...");
            sense = dev.ReadDma(out readBuf, out errorLba, false, 0, 1, dev.Timeout, out _);
            capabilities.SupportsReadDmaLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                              readBuf.Length                     > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba.Status, errorLba.Error, readBuf.Length);
            if(debug) capabilities.ReadDmaLbaData = readBuf;

            DicConsole.WriteLine("Trying READ DMA RETRY in LBA mode...");
            sense = dev.ReadDma(out readBuf, out errorLba, true, 0, 1, dev.Timeout, out _);
            capabilities.SupportsReadDmaRetryLba =
                !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 && readBuf.Length > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba.Status, errorLba.Error, readBuf.Length);
            if(debug) capabilities.ReadDmaRetryLbaData = readBuf;

            DicConsole.WriteLine("Trying SEEK in LBA mode...");
            sense                        = dev.Seek(out errorLba, 0, dev.Timeout, out _);
            capabilities.SupportsSeekLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}", sense,
                                      errorLba.Status, errorLba.Error);

            DicConsole.WriteLine("Trying READ SECTOR(S) in LBA48 mode...");
            sense = dev.Read(out readBuf, out AtaErrorRegistersLba48 errorLba48, 0, 1, dev.Timeout, out _);
            capabilities.SupportsReadLba48 = !sense && (errorLba48.Status & 0x01) != 0x01 && errorLba48.Error == 0 &&
                                             readBuf.Length                       > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba48.Status, errorLba48.Error, readBuf.Length);
            if(debug) capabilities.ReadLba48Data = readBuf;

            DicConsole.WriteLine("Trying READ DMA in LBA48 mode...");
            sense = dev.ReadDma(out readBuf, out errorLba48, 0, 1, dev.Timeout, out _);
            capabilities.SupportsReadDmaLba48 = !sense && (errorLba48.Status & 0x01) != 0x01 && errorLba48.Error == 0 &&
                                                readBuf.Length                       > 0;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba48.Status, errorLba48.Error, readBuf.Length);
            if(debug) capabilities.ReadDmaLba48Data = readBuf;

            // Send SET FEATURES before sending READ LONG commands, retrieve IDENTIFY again and
            // check if ECC size changed. Sector is set to 1 because without it most drives just return
            // CORRECTABLE ERROR for this command.
            dev.SetFeatures(out _, AtaFeatures.EnableReadLongVendorLength, 0, 0, 1, 0, dev.Timeout, out _);

            dev.AtaIdentify(out byte[] buffer, out _, dev.Timeout, out _);
            if(Identify.Decode(buffer).HasValue)
            {
                ataId = Identify.Decode(buffer).Value;
                if(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF)
                    capabilities.LongBlockSize = logicalsectorsize + ataId.EccBytes;

                if(ataId.UnformattedBPS > logicalsectorsize &&
                   (!(ataId.EccBytes != 0x0000 && ataId.EccBytes != 0xFFFF) || capabilities.LongBlockSize == 516))
                    capabilities.LongBlockSize = ataId.UnformattedBPS;
            }

            DicConsole.WriteLine("Trying READ LONG in CHS mode...");
            sense = dev.ReadLong(out readBuf, out errorChs, false, 0, 0, 1, capabilities.LongBlockSize ?? 0,
                                 dev.Timeout, out _);
            capabilities.SupportsReadLong = !sense && (errorChs.Status & 0x01) != 0x01 &&
                                            errorChs.Error                     == 0    &&
                                            readBuf.Length                     > 0     &&
                                            BitConverter.ToUInt64(readBuf, 0)  != checkCorrectRead;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorChs.Status, errorChs.Error, readBuf.Length);
            if(debug) capabilities.ReadLongData = readBuf;

            DicConsole.WriteLine("Trying READ LONG RETRY in CHS mode...");
            sense = dev.ReadLong(out readBuf, out errorChs, true, 0, 0, 1, capabilities.LongBlockSize ?? 0, dev.Timeout,
                                 out _);
            capabilities.SupportsReadLongRetry = !sense && (errorChs.Status & 0x01) != 0x01 && errorChs.Error == 0 &&
                                                 readBuf.Length                     > 0     &&
                                                 BitConverter.ToUInt64(readBuf, 0) !=
                                                 checkCorrectRead;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorChs.Status, errorChs.Error, readBuf.Length);
            if(debug) capabilities.ReadLongRetryData = readBuf;

            DicConsole.WriteLine("Trying READ LONG in LBA mode...");
            sense = dev.ReadLong(out readBuf, out errorLba, false, 0, capabilities.LongBlockSize ?? 0, dev.Timeout,
                                 out _);
            capabilities.SupportsReadLongLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                               readBuf.Length                     > 0     &&
                                               BitConverter.ToUInt64(readBuf, 0)  != checkCorrectRead;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba.Status, errorLba.Error, readBuf.Length);
            if(debug) capabilities.ReadLongLbaData = readBuf;

            DicConsole.WriteLine("Trying READ LONG RETRY in LBA mode...");
            sense = dev.ReadLong(out readBuf, out errorLba, true, 0, capabilities.LongBlockSize ?? 0, dev.Timeout,
                                 out _);
            capabilities.SupportsReadLongRetryLba = !sense && (errorLba.Status & 0x01) != 0x01 && errorLba.Error == 0 &&
                                                    readBuf.Length                     > 0     &&
                                                    BitConverter.ToUInt64(readBuf, 0) !=
                                                    checkCorrectRead;
            DicConsole.DebugWriteLine("ATA Report", "Sense = {0}, Status = 0x{1:X2}, Error = 0x{2:X2}, Length = {3}",
                                      sense, errorLba.Status, errorLba.Error, readBuf.Length);
            if(debug) capabilities.ReadLongRetryLbaData = readBuf;

            return capabilities;
        }

        public static byte[] ClearIdentify(byte[] buffer)
        {
            byte[] empty = new byte[512];

            Array.Copy(empty, 0, buffer, 20,  20);
            Array.Copy(empty, 0, buffer, 216, 8);
            Array.Copy(empty, 0, buffer, 224, 8);
            Array.Copy(empty, 0, buffer, 352, 40);

            return buffer;
        }
    }
}