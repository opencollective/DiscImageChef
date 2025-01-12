// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : ScsiInfo.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Core.
//
// --[ Description ] ----------------------------------------------------------
//
//     Retrieves the media info for a SCSI device.
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
using DiscImageChef.Checksums;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.Console;
using DiscImageChef.Core.Media.Detection;
using DiscImageChef.Decoders.CD;
using DiscImageChef.Decoders.DVD;
using DiscImageChef.Decoders.SCSI;
using DiscImageChef.Decoders.SCSI.MMC;
using DiscImageChef.Decoders.SCSI.SSC;
using DiscImageChef.Decoders.Sega;
using DiscImageChef.Decoders.Xbox;
using DiscImageChef.Devices;
using DeviceInfo = DiscImageChef.Core.Devices.Info.DeviceInfo;
using DMI = DiscImageChef.Decoders.Xbox.DMI;

namespace DiscImageChef.Core.Media.Info
{
    public class ScsiInfo
    {
        /// <summary>SHA256 of PlayStation 2 boot sectors, seen in PAL discs</summary>
        private const string PS2_PAL_HASH = "5d04ff236613e1d8adcf9c201874acd6f6deed1e04306558b86f91cfb626f39d";

        /// <summary>SHA256 of PlayStation 2 boot sectors, seen in Japanese, American, Malaysian and Korean discs</summary>
        private const string PS2_NTSC_HASH = "0bada1426e2c0351b872ef2a9ad2e5a0ac3918f4c53aa53329cb2911a8e16c23";

        /// <summary>SHA256 of PlayStation 2 boot sectors, seen in Japanese discs</summary>
        private const string PS2_JAPANESE_HASH = "b82bffb809070d61fe050b7e1545df53d8f3cc648257cdff7502bc0ba6b38870";

        private static readonly byte[] Ps3Id =
        {
            0x50, 0x6C, 0x61, 0x79, 0x53, 0x74, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x33, 0x00, 0x00, 0x00, 0x00
        };

        private static readonly byte[] Ps4Id =
        {
            0x50, 0x6C, 0x61, 0x79, 0x53, 0x74, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x34, 0x00, 0x00, 0x00, 0x00
        };

        private static readonly byte[] OperaId = {0x01, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x01};

        // Only present on bootable CDs, but those make more than 99% of all available
        private static readonly byte[] FmTownsBootId = {0x49, 0x50, 0x4C, 0x34, 0xEB, 0x55, 0x06};

        /// <summary>Present on first two seconds of second track, says "COPYRIGHT BANDAI"</summary>
        private static readonly byte[] PlaydiaCopyright =
        {
            0x43, 0x4F, 0x50, 0x59, 0x52, 0x49, 0x47, 0x48, 0x54, 0x20, 0x42, 0x41, 0x4E, 0x44, 0x41, 0x49
        };

        private static readonly byte[] PcEngineSignature =
        {
            0x50, 0x43, 0x20, 0x45, 0x6E, 0x67, 0x69, 0x6E, 0x65, 0x20, 0x43, 0x44, 0x2D, 0x52, 0x4F, 0x4D, 0x20,
            0x53, 0x59, 0x53, 0x54, 0x45, 0x4D
        };

        private static readonly byte[] PcFxSignature =
        {
            0x50, 0x43, 0x2D, 0x46, 0x58, 0x3A, 0x48, 0x75, 0x5F, 0x43, 0x44, 0x2D, 0x52, 0x4F, 0x4D
        };

        private static readonly byte[] AtariSignature =
        {
            0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54,
            0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41,
            0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49,
            0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x49, 0x52, 0x54, 0x41, 0x52, 0x41,
            0x20, 0x49, 0x50, 0x41, 0x52, 0x50, 0x56, 0x4F, 0x44, 0x45, 0x44, 0x20, 0x54, 0x41, 0x20, 0x41, 0x45,
            0x48, 0x44, 0x41, 0x52, 0x45, 0x41, 0x20, 0x52, 0x54
        };

        public ScsiInfo(Device dev)
        {
            if (dev.Type != DeviceType.SCSI && dev.Type != DeviceType.ATAPI) return;

            MediaType = MediaType.Unknown;
            MediaInserted = false;
            var resets = 0;
            var startOfFirstDataTrack = uint.MaxValue;
            bool sense;
            byte[] cmdBuf;
            byte[] senseBuf;
            bool containsFloppyPage;
            byte secondSessionFirstTrack = 0;

            if (dev.IsRemovable)
            {
                deviceGotReset:
                sense = dev.ScsiTestUnitReady(out senseBuf, dev.Timeout, out _);
                if (sense)
                {
                    var decSense = Sense.DecodeFixed(senseBuf);
                    if (decSense.HasValue)
                    {
                        // Just retry, for 5 times
                        if (decSense.Value.ASC == 0x29)
                        {
                            resets++;
                            if (resets < 5) goto deviceGotReset;
                        }

                        if (decSense.Value.ASC == 0x3A)
                        {
                            var leftRetries = 5;
                            while (leftRetries > 0)
                            {
                                //DicConsole.WriteLine("\rWaiting for drive to become ready");
                                Thread.Sleep(2000);
                                sense = dev.ScsiTestUnitReady(out senseBuf, dev.Timeout, out _);
                                if (!sense) break;

                                leftRetries--;
                            }

                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Please insert media in drive");
                                return;
                            }
                        }
                        else if (decSense.Value.ASC == 0x04 && decSense.Value.ASCQ == 0x01)
                        {
                            var leftRetries = 10;
                            while (leftRetries > 0)
                            {
                                //DicConsole.WriteLine("\rWaiting for drive to become ready");
                                Thread.Sleep(2000);
                                sense = dev.ScsiTestUnitReady(out senseBuf, dev.Timeout, out _);
                                if (!sense) break;

                                leftRetries--;
                            }

                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Error testing unit was ready:\n{0}",
                                    Sense.PrettifySense(senseBuf));
                                return;
                            }
                        }
                        else
                        {
                            DicConsole.ErrorWriteLine("Error testing unit was ready:\n{0}",
                                Sense.PrettifySense(senseBuf));
                            return;
                        }
                    }
                    else
                    {
                        DicConsole.ErrorWriteLine("Unknown testing unit was ready.");
                        return;
                    }
                }
            }

            MediaInserted = true;

            DeviceInfo = new DeviceInfo(dev);

            byte scsiMediumType = 0;
            byte scsiDensityCode = 0;
            containsFloppyPage = false;

            if (DeviceInfo.ScsiMode.HasValue)
            {
                scsiMediumType = (byte) DeviceInfo.ScsiMode.Value.Header.MediumType;
                if (DeviceInfo.ScsiMode.Value.Header.BlockDescriptors != null &&
                    DeviceInfo.ScsiMode.Value.Header.BlockDescriptors.Length >= 1)
                    scsiDensityCode = (byte) DeviceInfo.ScsiMode.Value.Header.BlockDescriptors[0].Density;

                if (DeviceInfo.ScsiMode.Value.Pages != null)
                    containsFloppyPage =
                        DeviceInfo.ScsiMode.Value.Pages.Aggregate(containsFloppyPage,
                            (current, modePage) =>
                                current | (modePage.Page == 0x05));
            }

            Blocks = 0;
            BlockSize = 0;

            switch (dev.ScsiType)
            {
                case PeripheralDeviceTypes.DirectAccess:
                case PeripheralDeviceTypes.MultiMediaDevice:
                case PeripheralDeviceTypes.OCRWDevice:
                case PeripheralDeviceTypes.OpticalDevice:
                case PeripheralDeviceTypes.SimplifiedDevice:
                case PeripheralDeviceTypes.WriteOnceDevice:
                    sense = dev.ReadCapacity(out cmdBuf, out senseBuf, dev.Timeout, out _);
                    if (!sense)
                    {
                        ReadCapacity = cmdBuf;
                        Blocks = (ulong) ((cmdBuf[0] << 24) + (cmdBuf[1] << 16) + (cmdBuf[2] << 8) + cmdBuf[3]);
                        BlockSize = (uint) ((cmdBuf[5] << 24) + (cmdBuf[5] << 16) + (cmdBuf[6] << 8) + cmdBuf[7]);
                    }

                    sense = dev.ReadCapacity16(out cmdBuf, out senseBuf, dev.Timeout, out _);
                    if (!sense) ReadCapacity16 = cmdBuf;

                    if (ReadCapacity == null || Blocks == 0xFFFFFFFF || Blocks == 0)
                    {
                        if (ReadCapacity16 == null && Blocks == 0)
                            if (dev.ScsiType != PeripheralDeviceTypes.MultiMediaDevice)
                            {
                                DicConsole.ErrorWriteLine("Unable to get media capacity");
                                DicConsole.ErrorWriteLine("{0}", Sense.PrettifySense(senseBuf));
                            }

                        if (ReadCapacity16 != null)
                        {
                            var temp = new byte[8];

                            Array.Copy(cmdBuf, 0, temp, 0, 8);
                            Array.Reverse(temp);
                            Blocks = BitConverter.ToUInt64(temp, 0);
                            BlockSize = (uint) ((cmdBuf[5] << 24) + (cmdBuf[5] << 16) + (cmdBuf[6] << 8) + cmdBuf[7]);
                        }
                    }

                    if (Blocks != 0 && BlockSize != 0) Blocks++;

                    break;
                case PeripheralDeviceTypes.SequentialAccess:
                    byte[] medBuf;

                    sense = dev.ReportDensitySupport(out var seqBuf, out senseBuf, false, dev.Timeout, out _);
                    if (!sense)
                    {
                        sense = dev.ReportDensitySupport(out medBuf, out senseBuf, true, dev.Timeout, out _);

                        if (!sense && !seqBuf.SequenceEqual(medBuf))
                        {
                            DensitySupport = seqBuf;
                            DensitySupportHeader = Decoders.SCSI.SSC.DensitySupport.DecodeDensity(seqBuf);
                        }
                    }

                    sense = dev.ReportDensitySupport(out seqBuf, out senseBuf, true, false, dev.Timeout, out _);
                    if (!sense)
                    {
                        sense = dev.ReportDensitySupport(out medBuf, out senseBuf, true, true, dev.Timeout, out _);

                        if (!sense && !seqBuf.SequenceEqual(medBuf))
                        {
                            MediaTypeSupport = medBuf;
                            MediaTypeSupportHeader = Decoders.SCSI.SSC.DensitySupport.DecodeMediumType(seqBuf);
                        }
                    }

                    // TODO: Get a machine where 16-byte CDBs don't get DID_ABORT
                    /*
                sense = dev.ReadAttribute(out seqBuf, out senseBuf, ScsiAttributeAction.List, 0, dev.Timeout, out _);
                if (sense)
                    DicConsole.ErrorWriteLine("SCSI READ ATTRIBUTE:\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                {
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_scsi_readattribute.bin", "SCSI READ ATTRIBUTE", seqBuf);
                }
                */
                    break;
            }

            if (dev.ScsiType == PeripheralDeviceTypes.MultiMediaDevice)
            {
                sense = dev.GetConfiguration(out cmdBuf, out senseBuf, 0, MmcGetConfigurationRt.Current, dev.Timeout,
                    out _);
                if (sense)
                {
                    DicConsole.DebugWriteLine("Media-Info command", "READ GET CONFIGURATION:\n{0}",
                        Sense.PrettifySense(senseBuf));
                }
                else
                {
                    MmcConfiguration = cmdBuf;
                    var ftr = Features.Separate(cmdBuf);

                    DicConsole.DebugWriteLine("Media-Info command", "GET CONFIGURATION current profile is {0:X4}h",
                        ftr.CurrentProfile);

                    switch (ftr.CurrentProfile)
                    {
                        case 0x0001:
                            MediaType = MediaType.GENERIC_HDD;
                            break;
                        case 0x0002:
                            switch (scsiMediumType)
                            {
                                case 0x01:
                                    MediaType = MediaType.PD650;
                                    break;
                                case 0x41:
                                    switch (Blocks)
                                    {
                                        case 58620544:
                                            MediaType = MediaType.REV120;
                                            break;
                                        case 17090880:
                                            MediaType = MediaType.REV35;
                                            break;
                                        default:
                                            // TODO: Unknown value
                                            MediaType = MediaType.REV70;
                                            break;
                                    }

                                    break;
                                default:
                                    MediaType = MediaType.Unknown;
                                    break;
                            }

                            break;
                        case 0x0005:
                            MediaType = MediaType.CDMO;
                            break;
                        case 0x0008:
                            MediaType = MediaType.CD;
                            break;
                        case 0x0009:
                            MediaType = MediaType.CDR;
                            break;
                        case 0x000A:
                            MediaType = MediaType.CDRW;
                            break;
                        case 0x0010:
                            MediaType = MediaType.DVDROM;
                            break;
                        case 0x0011:
                            MediaType = MediaType.DVDR;
                            break;
                        case 0x0012:
                            MediaType = MediaType.DVDRAM;
                            break;
                        case 0x0013:
                        case 0x0014:
                            MediaType = MediaType.DVDRW;
                            break;
                        case 0x0015:
                        case 0x0016:
                            MediaType = MediaType.DVDRDL;
                            break;
                        case 0x0017:
                            MediaType = MediaType.DVDRWDL;
                            break;
                        case 0x0018:
                            MediaType = MediaType.DVDDownload;
                            break;
                        case 0x001A:
                            MediaType = MediaType.DVDPRW;
                            break;
                        case 0x001B:
                            MediaType = MediaType.DVDPR;
                            break;
                        case 0x0020:
                            MediaType = MediaType.DDCD;
                            break;
                        case 0x0021:
                            MediaType = MediaType.DDCDR;
                            break;
                        case 0x0022:
                            MediaType = MediaType.DDCDRW;
                            break;
                        case 0x002A:
                            MediaType = MediaType.DVDPRWDL;
                            break;
                        case 0x002B:
                            MediaType = MediaType.DVDPRDL;
                            break;
                        case 0x0040:
                            MediaType = MediaType.BDROM;
                            break;
                        case 0x0041:
                        case 0x0042:
                            MediaType = MediaType.BDR;
                            break;
                        case 0x0043:
                            MediaType = MediaType.BDRE;
                            break;
                        case 0x0050:
                            MediaType = MediaType.HDDVDROM;
                            break;
                        case 0x0051:
                            MediaType = MediaType.HDDVDR;
                            break;
                        case 0x0052:
                            MediaType = MediaType.HDDVDRAM;
                            break;
                        case 0x0053:
                            MediaType = MediaType.HDDVDRW;
                            break;
                        case 0x0058:
                            MediaType = MediaType.HDDVDRDL;
                            break;
                        case 0x005A:
                            MediaType = MediaType.HDDVDRWDL;
                            break;
                    }
                }

                if (MediaType == MediaType.PD650 && Blocks == 1281856) MediaType = MediaType.PD650_WORM;

                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                    MmcDiscStructureFormat.RecognizedFormatLayers, 0, dev.Timeout, out _);
                if (sense)
                    DicConsole.DebugWriteLine("Media-Info command",
                        "READ DISC STRUCTURE: Recognized Format Layers\n{0}",
                        Sense.PrettifySense(senseBuf));
                else RecognizedFormatLayers = cmdBuf;

                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                    MmcDiscStructureFormat.WriteProtectionStatus, 0, dev.Timeout, out _);
                if (sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Write Protection Status\n{0}",
                        Sense.PrettifySense(senseBuf));
                else WriteProtectionStatus = cmdBuf;

                // More like a drive information
                /*
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.CapabilityList, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Capability List\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_capabilitylist.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                */

                #region All DVD and HD DVD types

                if (MediaType == MediaType.DVDDownload || MediaType == MediaType.DVDPR ||
                    MediaType == MediaType.DVDPRDL || MediaType == MediaType.DVDPRW ||
                    MediaType == MediaType.DVDPRWDL ||
                    MediaType == MediaType.DVDR || MediaType == MediaType.DVDRAM ||
                    MediaType == MediaType.DVDRDL ||
                    MediaType == MediaType.DVDROM || MediaType == MediaType.DVDRW ||
                    MediaType == MediaType.DVDRWDL ||
                    MediaType == MediaType.HDDVDR || MediaType == MediaType.HDDVDRAM ||
                    MediaType == MediaType.HDDVDRDL || MediaType == MediaType.HDDVDROM ||
                    MediaType == MediaType.HDDVDRW || MediaType == MediaType.HDDVDRWDL)
                {
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.PhysicalInformation, 0, dev.Timeout, out _);
                    if (sense)
                    {
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: PFI\n{0}",
                            Sense.PrettifySense(senseBuf));
                    }
                    else
                    {
                        DvdPfi = cmdBuf;
                        DecodedPfi = PFI.Decode(cmdBuf);
                        if (DecodedPfi.HasValue)
                            if (MediaType == MediaType.DVDROM)
                                switch (DecodedPfi.Value.DiskCategory)
                                {
                                    case DiskCategory.DVDPR:
                                        MediaType = MediaType.DVDPR;
                                        break;
                                    case DiskCategory.DVDPRDL:
                                        MediaType = MediaType.DVDPRDL;
                                        break;
                                    case DiskCategory.DVDPRW:
                                        MediaType = MediaType.DVDPRW;
                                        break;
                                    case DiskCategory.DVDPRWDL:
                                        MediaType = MediaType.DVDPRWDL;
                                        break;
                                    case DiskCategory.DVDR:
                                        MediaType = DecodedPfi.Value.PartVersion == 6
                                            ? MediaType.DVDRDL
                                            : MediaType.DVDR;
                                        break;
                                    case DiskCategory.DVDRAM:
                                        MediaType = MediaType.DVDRAM;
                                        break;
                                    default:
                                        MediaType = MediaType.DVDROM;
                                        break;
                                    case DiskCategory.DVDRW:
                                        MediaType = DecodedPfi.Value.PartVersion == 3
                                            ? MediaType.DVDRWDL
                                            : MediaType.DVDRW;
                                        break;
                                    case DiskCategory.HDDVDR:
                                        MediaType = MediaType.HDDVDR;
                                        break;
                                    case DiskCategory.HDDVDRAM:
                                        MediaType = MediaType.HDDVDRAM;
                                        break;
                                    case DiskCategory.HDDVDROM:
                                        MediaType = MediaType.HDDVDROM;
                                        break;
                                    case DiskCategory.HDDVDRW:
                                        MediaType = MediaType.HDDVDRW;
                                        break;
                                    case DiskCategory.Nintendo:
                                        MediaType = DecodedPfi.Value.DiscSize == DVDSize.Eighty
                                            ? MediaType.GOD
                                            : MediaType.WOD;
                                        break;
                                    case DiskCategory.UMD:
                                        MediaType = MediaType.UMD;
                                        break;
                                }
                    }

                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.DiscManufacturingInformation, 0, dev.Timeout,
                        out _);
                    if (sense)
                    {
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DMI\n{0}",
                            Sense.PrettifySense(senseBuf));
                    }
                    else
                    {
                        DvdDmi = cmdBuf;
                        if (DMI.IsXbox(cmdBuf))
                        {
                            MediaType = MediaType.XGD;
                        }
                        else if (DMI.IsXbox360(cmdBuf))
                        {
                            MediaType = MediaType.XGD2;

                            // All XGD3 all have the same number of blocks
                            if (Blocks == 25063 || // Locked (or non compatible drive)
                                Blocks == 4229664 || // Xtreme unlock
                                Blocks == 4246304) // Wxripper unlock
                                MediaType = MediaType.XGD3;
                        }
                    }
                }

                #endregion All DVD and HD DVD types

                #region DVD-ROM

                if (MediaType == MediaType.DVDDownload || MediaType == MediaType.DVDROM)
                {
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.CopyrightInformation, 0, dev.Timeout, out _);
                    if (sense)
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: CMI\n{0}",
                            Sense.PrettifySense(senseBuf));
                    else DvdCmi = cmdBuf;
                }

                #endregion DVD-ROM

                switch (MediaType)
                {
                    #region DVD-ROM and HD DVD-ROM

                    case MediaType.DVDDownload:
                    case MediaType.DVDROM:
                    case MediaType.HDDVDROM:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.BurstCuttingArea, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: BCA\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdBca = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.DvdAacs, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DVD AACS\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdAacs = cmdBuf;
                        break;

                    #endregion DVD-ROM and HD DVD-ROM

                    #region DVD-RAM and HD DVD-RAM

                    case MediaType.DVDRAM:
                    case MediaType.HDDVDRAM:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.DvdramDds, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DDS\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdRamDds = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.DvdramMediumStatus, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Medium Status\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdRamCartridgeStatus = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.DvdramSpareAreaInformation, 0, dev.Timeout,
                            out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: SAI\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdRamSpareArea = cmdBuf;

                        break;

                    #endregion DVD-RAM and HD DVD-RAM

                    #region DVD-R and HD DVD-R

                    case MediaType.DVDR:
                    case MediaType.HDDVDR:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.LastBorderOutRmd, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Last-Out Border RMD\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else LastBorderOutRmd = cmdBuf;
                        break;

                    #endregion DVD-R and HD DVD-R
                }

                #region Require drive authentication, won't work

                /*
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.DiscKey, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Disc Key\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_dvd_disckey.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.SectorCopyrightInformation, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Sector CMI\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_dvd_sectorcmi.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.MediaIdentifier, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Media ID\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_dvd_mediaid.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.MediaKeyBlock, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: MKB\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_dvd_mkb.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSVolId, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS Volume ID\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacsvolid.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSMediaSerial, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS Media Serial Number\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacssn.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSMediaId, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS Media ID\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacsmediaid.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSMKB, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS MKB\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacsmkb.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSLBAExtents, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS LBA Extents\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacslbaextents.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSMKBCPRM, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS CPRM MKB\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacscprmmkb.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.DVD, 0, 0, MmcDiscStructureFormat.AACSDataKeys, 0, dev.Timeout, out _);
                if(sense)
                    DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: AACS Data Keys\n{0}", Decoders.SCSI.Sense.PrettifySense(senseBuf));
                else
                    DataFile.WriteTo("Media-Info command", outputPrefix, "_readdiscstructure_aacsdatakeys.bin", "SCSI READ DISC STRUCTURE", cmdBuf);
                */

                #endregion Require drive authentication, won't work

                #region DVD-R and DVD-RW

                if (MediaType == MediaType.DVDR || MediaType == MediaType.DVDRW)
                {
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.PreRecordedInfo, 0, dev.Timeout, out _);
                    if (sense)
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Pre-Recorded Info\n{0}",
                            Sense.PrettifySense(senseBuf));
                    else DvdPreRecordedInfo = cmdBuf;
                }

                #endregion DVD-R and DVD-RW

                switch (MediaType)
                {
                    #region DVD-R, DVD-RW and HD DVD-R

                    case MediaType.DVDR:
                    case MediaType.DVDRW:
                    case MediaType.HDDVDR:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.DvdrMediaIdentifier, 0, dev.Timeout,
                            out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DVD-R Media ID\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdrMediaIdentifier = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.DvdrPhysicalInformation, 0, dev.Timeout,
                            out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DVD-R PFI\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdrPhysicalInformation = cmdBuf;

                        break;

                    #endregion DVD-R, DVD-RW and HD DVD-R

                    #region All DVD+

                    case MediaType.DVDPR:
                    case MediaType.DVDPRDL:
                    case MediaType.DVDPRW:
                    case MediaType.DVDPRWDL:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.Adip, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: ADIP\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdPlusAdip = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.Dcb, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DCB\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdPlusDcb = cmdBuf;
                        break;

                    #endregion All DVD+

                    #region HD DVD-ROM

                    case MediaType.HDDVDROM:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.HddvdCopyrightInformation, 0, dev.Timeout,
                            out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: HDDVD CMI\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else HddvdCopyrightInformation = cmdBuf;
                        break;

                    #endregion HD DVD-ROM
                }

                #region HD DVD-R

                if (MediaType == MediaType.HDDVDR)
                {
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.HddvdrMediumStatus, 0, dev.Timeout, out _);
                    if (sense)
                        DicConsole.DebugWriteLine("Media-Info command",
                            "READ DISC STRUCTURE: HDDVD-R Medium Status\n{0}",
                            Sense.PrettifySense(senseBuf));
                    else HddvdrMediumStatus = cmdBuf;
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.HddvdrLastRmd, 0, dev.Timeout, out _);
                    if (sense)
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Last RMD\n{0}",
                            Sense.PrettifySense(senseBuf));
                    else HddvdrLastRmd = cmdBuf;
                }

                #endregion HD DVD-R

                #region DVD-R DL, DVD-RW DL, DVD+R DL, DVD+RW DL

                if (MediaType == MediaType.DVDPRDL || MediaType == MediaType.DVDRDL || MediaType == MediaType.DVDRWDL ||
                    MediaType == MediaType.DVDPRWDL)
                {
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.DvdrLayerCapacity, 0, dev.Timeout, out _);
                    if (sense)
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Layer Capacity\n{0}",
                            Sense.PrettifySense(senseBuf));
                    else DvdrLayerCapacity = cmdBuf;
                }

                #endregion DVD-R DL, DVD-RW DL, DVD+R DL, DVD+RW DL

                switch (MediaType)
                {
                    #region DVD-R DL

                    case MediaType.DVDRDL:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.MiddleZoneStart, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Middle Zone Start\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdrDlMiddleZoneStart = cmdBuf;
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.JumpIntervalSize, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Jump Interval Size\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdrDlJumpIntervalSize = cmdBuf;
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.ManualLayerJumpStartLba, 0, dev.Timeout,
                            out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Manual Layer Jump Start LBA\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdrDlManualLayerJumpStartLba = cmdBuf;
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                            MmcDiscStructureFormat.RemapAnchorPoint, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Remap Anchor Point\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else DvdrDlRemapAnchorPoint = cmdBuf;
                        break;

                    #endregion DVD-R DL

                    #region All Blu-ray

                    case MediaType.BDR:
                    case MediaType.BDRE:
                    case MediaType.BDROM:
                    case MediaType.BDRXL:
                    case MediaType.BDREXL:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.DiscInformation, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DI\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayDiscInformation = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.Pac, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: PAC\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayPac = cmdBuf;
                        break;

                    #endregion All Blu-ray
                }

                switch (MediaType)
                {
                    #region BD-ROM only

                    case MediaType.BDROM:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.BdBurstCuttingArea, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: BCA\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayBurstCuttingArea = cmdBuf;

                        break;

                    #endregion BD-ROM only

                    #region Writable Blu-ray only

                    case MediaType.BDR:
                    case MediaType.BDRE:
                    case MediaType.BDRXL:
                    case MediaType.BDREXL:
                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.BdDds, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DDS\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayDds = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.CartridgeStatus, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Cartridge Status\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayCartridgeStatus = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.BdSpareAreaInformation, 0, dev.Timeout,
                            out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command",
                                "READ DISC STRUCTURE: Spare Area Information\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BluraySpareAreaInformation = cmdBuf;

                        sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Bd, 0, 0,
                            MmcDiscStructureFormat.RawDfl, 0, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: Raw DFL\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayRawDfl = cmdBuf;
                        sense = dev.ReadDiscInformation(out cmdBuf, out senseBuf,
                            MmcDiscInformationDataTypes.TrackResources, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC INFORMATION 001b\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayTrackResources = cmdBuf;

                        sense = dev.ReadDiscInformation(out cmdBuf, out senseBuf,
                            MmcDiscInformationDataTypes.PowResources, dev.Timeout, out _);
                        if (sense)
                            DicConsole.DebugWriteLine("Media-Info command", "READ DISC INFORMATION 010b\n{0}",
                                Sense.PrettifySense(senseBuf));
                        else BlurayPowResources = cmdBuf;

                        break;

                    #endregion Writable Blu-ray only

                    #region CDs

                    case MediaType.CD:
                    case MediaType.CDR:
                    case MediaType.CDROM:
                    case MediaType.CDRW:
                    case MediaType.Unknown:
                        // We discarded all discs that falsify a TOC before requesting a real TOC
                        // No TOC, no CD (or an empty one)
                        var tocSense = dev.ReadTocPmaAtip(out cmdBuf, out senseBuf, false, 0, 0, dev.Timeout, out _);
                        if (tocSense)
                        {
                            DicConsole.DebugWriteLine("Media-Info command", "READ TOC/PMA/ATIP: TOC\n{0}",
                                Sense.PrettifySense(senseBuf));
                        }
                        else
                        {
                            Toc = cmdBuf;
                            DecodedToc = TOC.Decode(cmdBuf);

                            // As we have a TOC we know it is a CD
                            if (MediaType == MediaType.Unknown) MediaType = MediaType.CD;
                        }

                        // ATIP exists on blank CDs
                        sense = dev.ReadAtip(out cmdBuf, out senseBuf, dev.Timeout, out _);
                        if (sense)
                        {
                            DicConsole.DebugWriteLine("Media-Info command", "READ TOC/PMA/ATIP: ATIP\n{0}",
                                Sense.PrettifySense(senseBuf));
                        }
                        else
                        {
                            Atip = cmdBuf;
                            DecodedAtip = ATIP.Decode(cmdBuf);
                            if (DecodedAtip.HasValue)
                                // Only CD-R and CD-RW have ATIP
                                MediaType = DecodedAtip.Value.DiscType ? MediaType.CDRW : MediaType.CDR;
                        }

                        // We got a TOC, get information about a recorded/mastered CD
                        if (!tocSense)
                        {
                            sense = dev.ReadDiscInformation(out cmdBuf, out senseBuf,
                                MmcDiscInformationDataTypes.DiscInformation, dev.Timeout,
                                out _);
                            if (sense)
                            {
                                DicConsole.DebugWriteLine("Media-Info command", "READ DISC INFORMATION 000b\n{0}",
                                    Sense.PrettifySense(senseBuf));
                            }
                            else
                            {
                                CompactDiscInformation = cmdBuf;
                                DecodedCompactDiscInformation = DiscInformation.Decode000b(cmdBuf);
                                if (DecodedCompactDiscInformation.HasValue)
                                    if (MediaType == MediaType.CD)
                                        switch (DecodedCompactDiscInformation.Value.DiscType)
                                        {
                                            case 0x10:
                                                MediaType = MediaType.CDI;
                                                break;
                                            case 0x20:
                                                MediaType = MediaType.CDROMXA;
                                                break;
                                        }
                            }

                            var sessions = 1;
                            var firstTrackLastSession = 0;

                            sense = dev.ReadSessionInfo(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.DebugWriteLine("Media-Info command", "READ TOC/PMA/ATIP: Session info\n{0}",
                                    Sense.PrettifySense(senseBuf));
                            }
                            else
                            {
                                Session = cmdBuf;
                                DecodedSession = Decoders.CD.Session.Decode(cmdBuf);
                                if (DecodedSession.HasValue)
                                {
                                    sessions = DecodedSession.Value.LastCompleteSession;
                                    firstTrackLastSession = DecodedSession.Value.TrackDescriptors[0].TrackNumber;
                                }
                            }

                            if (MediaType == MediaType.CD)
                            {
                                var hasDataTrack = false;
                                var hasAudioTrack = false;
                                var allFirstSessionTracksAreAudio = true;
                                var hasVideoTrack = false;

                                if (DecodedToc.HasValue)
                                    foreach (var track in DecodedToc.Value.TrackDescriptors)
                                    {
                                        if (track.TrackNumber == 1 &&
                                            ((TocControl) (track.CONTROL & 0x0D) == TocControl.DataTrack ||
                                             (TocControl) (track.CONTROL & 0x0D) == TocControl.DataTrackIncremental))
                                            allFirstSessionTracksAreAudio &= firstTrackLastSession != 1;

                                        if ((TocControl) (track.CONTROL & 0x0D) == TocControl.DataTrack ||
                                            (TocControl) (track.CONTROL & 0x0D) == TocControl.DataTrackIncremental)
                                        {
                                            if (track.TrackStartAddress < startOfFirstDataTrack)
                                                startOfFirstDataTrack = track.TrackStartAddress;
                                            hasDataTrack = true;
                                            allFirstSessionTracksAreAudio &= track.TrackNumber >= firstTrackLastSession;
                                        }
                                        else
                                        {
                                            hasAudioTrack = true;
                                        }

                                        hasVideoTrack |= track.ADR == 4;
                                    }

                                if (hasDataTrack && hasAudioTrack && allFirstSessionTracksAreAudio && sessions == 2)
                                    MediaType = MediaType.CDPLUS;
                                if (!hasDataTrack && hasAudioTrack && sessions == 1) MediaType = MediaType.CDDA;
                                if (hasDataTrack && !hasAudioTrack && sessions == 1) MediaType = MediaType.CDROM;
                                if (hasVideoTrack && !hasDataTrack && sessions == 1) MediaType = MediaType.CDV;
                            }

                            sense = dev.ReadRawToc(out cmdBuf, out senseBuf, 1, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.DebugWriteLine("Media-Info command", "READ TOC/PMA/ATIP: Raw TOC\n{0}",
                                    Sense.PrettifySense(senseBuf));
                            }
                            else
                            {
                                RawToc = cmdBuf;

                                FullToc = FullTOC.Decode(cmdBuf);
                                if (FullToc.HasValue)
                                {
                                    var a0Track =
                                        FullToc.Value.TrackDescriptors
                                            .FirstOrDefault(t => t.POINT == 0xA0 && t.ADR == 1);
                                    if (a0Track.POINT == 0xA0)
                                        switch (a0Track.PSEC)
                                        {
                                            case 0x10:
                                                MediaType = MediaType.CDI;
                                                break;
                                            case 0x20:
                                                MediaType = MediaType.CDROMXA;
                                                break;
                                        }

                                    if (FullToc.Value.TrackDescriptors.Any(t => t.SessionNumber == 2))
                                        secondSessionFirstTrack = FullToc
                                            .Value.TrackDescriptors
                                            .Where(t => t.SessionNumber == 2).Min(t => t.POINT);
                                }
                            }

                            sense = dev.ReadPma(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                                DicConsole.DebugWriteLine("Media-Info command", "READ TOC/PMA/ATIP: PMA\n{0}",
                                    Sense.PrettifySense(senseBuf));
                            else Pma = cmdBuf;

                            sense = dev.ReadCdText(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.DebugWriteLine("Media-Info command", "READ TOC/PMA/ATIP: CD-TEXT\n{0}",
                                    Sense.PrettifySense(senseBuf));
                            }
                            else
                            {
                                CdTextLeadIn = cmdBuf;
                                DecodedCdTextLeadIn = CDTextOnLeadIn.Decode(cmdBuf);
                            }

                            sense = dev.ReadMcn(out var mcn, out _, out _, dev.Timeout, out _);
                            if (!sense && mcn != null && mcn != "0000000000000") Mcn = mcn;

                            Isrcs = new Dictionary<byte, string>();
                            for (var i = DecodedToc.Value.FirstTrack; i <= DecodedToc.Value.LastTrack; i++)
                            {
                                sense = dev.ReadIsrc(i, out var isrc, out _, out _, dev.Timeout, out _);
                                if (!sense && isrc != null && isrc != "000000000000") Isrcs.Add(i, isrc);
                            }

                            if (Isrcs.Count == 0) Isrcs = null;
                        }

                        break;

                    #endregion CDs
                }

                #region Nintendo

                if (MediaType == MediaType.Unknown && Blocks > 0)
                {
                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.PhysicalInformation, 0, dev.Timeout, out _);
                    if (sense)
                    {
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: PFI\n{0}",
                            Sense.PrettifySense(senseBuf));
                    }
                    else
                    {
                        DvdPfi = cmdBuf;
                        var nintendoPfi = PFI.Decode(cmdBuf);
                        if (nintendoPfi != null)
                        {
                            DicConsole.WriteLine("PFI:\n{0}", PFI.Prettify(cmdBuf));
                            if (nintendoPfi.Value.DiskCategory == DiskCategory.Nintendo &&
                                nintendoPfi.Value.PartVersion == 15)
                                switch (nintendoPfi.Value.DiscSize)
                                {
                                    case DVDSize.Eighty:
                                        MediaType = MediaType.GOD;
                                        break;
                                    case DVDSize.OneTwenty:
                                        MediaType = MediaType.WOD;
                                        break;
                                }
                        }
                    }

                    sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                        MmcDiscStructureFormat.DiscManufacturingInformation, 0, dev.Timeout,
                        out _);
                    if (sense)
                        DicConsole.DebugWriteLine("Media-Info command", "READ DISC STRUCTURE: DMI\n{0}",
                            Sense.PrettifySense(senseBuf));
                    else DvdDmi = cmdBuf;
                }

                #endregion Nintendo
            }

            sense = dev.ReadMediaSerialNumber(out cmdBuf, out senseBuf, dev.Timeout, out _);
            if (sense)
            {
                DicConsole.DebugWriteLine("Media-Info command", "READ MEDIA SERIAL NUMBER\n{0}",
                    Sense.PrettifySense(senseBuf));
            }
            else
            {
                if (cmdBuf.Length >= 4) MediaSerialNumber = cmdBuf;
            }

            switch (MediaType)
            {
                #region Xbox

                case MediaType.XGD:
                case MediaType.XGD2:
                case MediaType.XGD3:
                    // We need to get INQUIRY to know if it is a Kreon drive
                    sense = dev.ScsiInquiry(out var inqBuffer, out senseBuf);
                    if (!sense)
                    {
                        var inq = Inquiry.Decode(inqBuffer);
                        if (inq.HasValue && inq.Value.KreonPresent)
                        {
                            sense = dev.KreonExtractSs(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                                DicConsole.DebugWriteLine("Media-Info command", "KREON EXTRACT SS:\n{0}",
                                    Sense.PrettifySense(senseBuf));
                            else XboxSecuritySector = cmdBuf;

                            DecodedXboxSecuritySector = SS.Decode(cmdBuf);

                            // Get video partition size
                            DicConsole.DebugWriteLine("Dump-media command", "Getting video partition size");
                            sense = dev.KreonLock(out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot lock drive, not continuing.");
                                return;
                            }

                            sense = dev.ReadCapacity(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot get disc capacity.");
                                return;
                            }

                            var totalSize =
                                (ulong) ((cmdBuf[0] << 24) + (cmdBuf[1] << 16) + (cmdBuf[2] << 8) + cmdBuf[3]);
                            sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                                MmcDiscStructureFormat.PhysicalInformation, 0, 0, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot get PFI.");
                                return;
                            }

                            DicConsole.DebugWriteLine("Dump-media command", "Video partition total size: {0} sectors",
                                totalSize);
                            ulong l0Video = PFI.Decode(cmdBuf).Value.Layer0EndPSN -
                                            PFI.Decode(cmdBuf).Value.DataAreaStartPSN + 1;
                            var l1Video = totalSize - l0Video + 1;

                            // Get game partition size
                            DicConsole.DebugWriteLine("Dump-media command", "Getting game partition size");
                            sense = dev.KreonUnlockXtreme(out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot unlock drive, not continuing.");
                                return;
                            }

                            sense = dev.ReadCapacity(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot get disc capacity.");
                                return;
                            }

                            var gameSize =
                                (ulong) ((cmdBuf[0] << 24) + (cmdBuf[1] << 16) + (cmdBuf[2] << 8) + cmdBuf[3]) + 1;
                            DicConsole.DebugWriteLine("Dump-media command", "Game partition total size: {0} sectors",
                                gameSize);

                            // Get middle zone size
                            DicConsole.DebugWriteLine("Dump-media command", "Getting middle zone size");
                            sense = dev.KreonUnlockWxripper(out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot unlock drive, not continuing.");
                                return;
                            }

                            sense = dev.ReadCapacity(out cmdBuf, out senseBuf, dev.Timeout, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot get disc capacity.");
                                return;
                            }

                            totalSize = (ulong) ((cmdBuf[0] << 24) + (cmdBuf[1] << 16) + (cmdBuf[2] << 8) + cmdBuf[3]);
                            sense = dev.ReadDiscStructure(out cmdBuf, out senseBuf, MmcDiscStructureMediaType.Dvd, 0, 0,
                                MmcDiscStructureFormat.PhysicalInformation, 0, 0, out _);
                            if (sense)
                            {
                                DicConsole.ErrorWriteLine("Cannot get PFI.");
                                return;
                            }

                            DicConsole.DebugWriteLine("Dump-media command", "Unlocked total size: {0} sectors",
                                totalSize);
                            var middleZone =
                                totalSize -
                                (PFI.Decode(cmdBuf).Value.Layer0EndPSN -
                                 PFI.Decode(cmdBuf).Value.DataAreaStartPSN + 1) - gameSize + 1;

                            totalSize = l0Video + l1Video + middleZone * 2 + gameSize;
                            var layerBreak = l0Video + middleZone + gameSize / 2;

                            XgdInfo = new XgdInfo
                            {
                                L0Video = l0Video,
                                L1Video = l1Video,
                                MiddleZone = middleZone,
                                GameSize = gameSize,
                                TotalSize = totalSize,
                                LayerBreak = layerBreak
                            };
                        }
                    }

                    break;

                #endregion Xbox

                case MediaType.Unknown:
                    MediaType = MediaTypeFromScsi.Get((byte) dev.ScsiType, dev.Manufacturer, dev.Model, scsiMediumType,
                        scsiDensityCode, Blocks, BlockSize);
                    break;
            }

            if (MediaType == MediaType.Unknown && dev.IsUsb && containsFloppyPage) MediaType = MediaType.FlashDrive;

            if (DeviceInfo.ScsiType != PeripheralDeviceTypes.MultiMediaDevice) return;

            byte[] sector0 = null;
            byte[] sector1 = null;
            byte[] ps2BootSectors = null;
            byte[] playdia1 = null;
            byte[] playdia2 = null;
            byte[] firstDataSectorNotZero = null;
            byte[] secondDataSectorNotZero = null;
            byte[] firstTrackSecondSession = null;
            byte[] firstTrackSecondSessionAudio = null;
            byte[] videoNowColorFrame = null;

            if (secondSessionFirstTrack != 0 && DecodedToc.HasValue &&
                DecodedToc.Value.TrackDescriptors.Any(t => t.TrackNumber == secondSessionFirstTrack))
            {
                var firstSectorSecondSessionFirstTrack = DecodedToc
                    .Value.TrackDescriptors
                    .First(t => t.TrackNumber == secondSessionFirstTrack)
                    .TrackStartAddress;

                sense = dev.ReadCd(out cmdBuf, out senseBuf, firstSectorSecondSessionFirstTrack, 2352, 1,
                    MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders, true, true,
                    MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                if (!sense && !dev.Error)
                {
                    firstTrackSecondSession = cmdBuf;
                }
                else
                {
                    sense = dev.ReadCd(out cmdBuf, out senseBuf, firstSectorSecondSessionFirstTrack, 2352, 1,
                        MmcSectorTypes.Cdda, false, false, true, MmcHeaderCodes.None, true, true,
                        MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                    if (!sense && !dev.Error) firstTrackSecondSession = cmdBuf;
                }

                sense = dev.ReadCd(out cmdBuf, out senseBuf, firstSectorSecondSessionFirstTrack - 1, 2352, 3,
                    MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders, true, true,
                    MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                if (!sense && !dev.Error)
                {
                    firstTrackSecondSessionAudio = cmdBuf;
                }
                else
                {
                    sense = dev.ReadCd(out cmdBuf, out senseBuf, firstSectorSecondSessionFirstTrack - 1, 2352, 3,
                        MmcSectorTypes.Cdda, false, false, true, MmcHeaderCodes.None, true, true,
                        MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                    if (!sense && !dev.Error) firstTrackSecondSessionAudio = cmdBuf;
                }
            }

            videoNowColorFrame = new byte[9 * 2352];
            for (var i = 0; i < 9; i++)
            {
                sense = dev.ReadCd(out cmdBuf, out senseBuf, (uint) i, 2352, 1, MmcSectorTypes.AllTypes, false, false,
                    true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None, MmcSubchannel.None,
                    dev.Timeout, out _);

                if (sense || dev.Error)
                {
                    sense = dev.ReadCd(out cmdBuf, out senseBuf, (uint) i, 2352, 1, MmcSectorTypes.Cdda, false, false,
                        true, MmcHeaderCodes.None, true, true, MmcErrorField.None, MmcSubchannel.None,
                        dev.Timeout, out _);

                    if (sense || !dev.Error)
                    {
                        videoNowColorFrame = null;
                        break;
                    }
                }

                Array.Copy(cmdBuf, 0, videoNowColorFrame, i * 2352, 2352);
            }

            // Check for hidden data before start of track 1
            if (DecodedToc.HasValue &&
                DecodedToc.Value.TrackDescriptors.FirstOrDefault(t => t.TrackNumber == 1).TrackStartAddress > 0)
            {
                sense = dev.ReadCd(out cmdBuf, out senseBuf, 0, 2352, 1, MmcSectorTypes.AllTypes, false, false, true,
                    MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None, MmcSubchannel.None,
                    dev.Timeout, out _);

                if (!dev.Error && !sense)
                {
                    sector0 = cmdBuf;

                    sense = dev.ReadCd(out cmdBuf, out senseBuf, 16, 2352, 1, MmcSectorTypes.AllTypes, false, false,
                        true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None,
                        MmcSubchannel.None, dev.Timeout, out _);

                    if (!dev.Error && !sense)
                        if (MMC.IsCdi(sector0, cmdBuf))
                            MediaType = MediaType.CDIREADY;
                }
            }

            sector0 = null;

            switch (MediaType)
            {
                case MediaType.CD:
                case MediaType.CDDA:
                case MediaType.CDPLUS:
                case MediaType.CDROM:
                case MediaType.CDROMXA:
                {
                    sense = dev.ReadCd(out cmdBuf, out senseBuf, 0, 2352, 1, MmcSectorTypes.AllTypes, false, false,
                        true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None,
                        MmcSubchannel.None, dev.Timeout, out _);

                    if (!sense && !dev.Error)
                    {
                        sector0 = new byte[2048];
                        Array.Copy(cmdBuf, 16, sector0, 0, 2048);

                        sense = dev.ReadCd(out cmdBuf, out senseBuf, 1, 2352, 1, MmcSectorTypes.AllTypes, false, false,
                            true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None,
                            MmcSubchannel.None, dev.Timeout, out _);

                        if (!sense && !dev.Error)
                        {
                            sector1 = new byte[2048];
                            Array.Copy(cmdBuf, 16, sector1, 0, 2048);
                        }

                        sense = dev.ReadCd(out cmdBuf, out senseBuf, 4200, 2352, 1, MmcSectorTypes.AllTypes, false,
                            false, true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None,
                            MmcSubchannel.None, dev.Timeout, out _);

                        if (!sense && !dev.Error)
                        {
                            playdia1 = new byte[2048];
                            Array.Copy(cmdBuf, 24, playdia1, 0, 2048);
                        }

                        sense = dev.ReadCd(out cmdBuf, out senseBuf, 4201, 2352, 1, MmcSectorTypes.AllTypes, false,
                            false, true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None,
                            MmcSubchannel.None, dev.Timeout, out _);

                        if (!sense && !dev.Error)
                        {
                            playdia2 = new byte[2048];
                            Array.Copy(cmdBuf, 24, playdia2, 0, 2048);
                        }

                        if (startOfFirstDataTrack != uint.MaxValue)
                        {
                            sense = dev.ReadCd(out cmdBuf, out senseBuf, startOfFirstDataTrack, 2352, 1,
                                MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                                true, true, MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                firstDataSectorNotZero = new byte[2048];
                                Array.Copy(cmdBuf, 16, firstDataSectorNotZero, 0, 2048);
                            }

                            sense = dev.ReadCd(out cmdBuf, out senseBuf, startOfFirstDataTrack + 1, 2352, 1,
                                MmcSectorTypes.AllTypes, false, false, true, MmcHeaderCodes.AllHeaders,
                                true, true, MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                secondDataSectorNotZero = new byte[2048];
                                Array.Copy(cmdBuf, 16, secondDataSectorNotZero, 0, 2048);
                            }
                        }

                        var ps2Ms = new MemoryStream();
                        for (uint p = 0; p < 12; p++)
                        {
                            sense = dev.ReadCd(out cmdBuf, out senseBuf, p, 2352, 1, MmcSectorTypes.AllTypes, false,
                                false, true, MmcHeaderCodes.AllHeaders, true, true, MmcErrorField.None,
                                MmcSubchannel.None, dev.Timeout, out _);

                            if (sense || dev.Error) break;

                            ps2Ms.Write(cmdBuf, cmdBuf[0x0F] == 0x02 ? 24 : 16, 2048);
                        }

                        if (ps2Ms.Length == 0x6000) ps2BootSectors = ps2Ms.ToArray();
                    }
                    else
                    {
                        sense = dev.ReadCd(out cmdBuf, out senseBuf, 0, 2324, 1, MmcSectorTypes.Mode2, false, false,
                            true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                            MmcSubchannel.None, dev.Timeout, out _);

                        if (!sense && !dev.Error)
                        {
                            sector0 = new byte[2048];
                            Array.Copy(cmdBuf, 0, sector0, 0, 2048);

                            sense = dev.ReadCd(out cmdBuf, out senseBuf, 1, 2324, 1, MmcSectorTypes.Mode2, false, false,
                                true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                MmcSubchannel.None, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                sector1 = new byte[2048];
                                Array.Copy(cmdBuf, 1, sector0, 0, 2048);
                            }

                            sense = dev.ReadCd(out cmdBuf, out senseBuf, 4200, 2324, 1, MmcSectorTypes.Mode2, false,
                                false, true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                MmcSubchannel.None, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                playdia1 = new byte[2048];
                                Array.Copy(cmdBuf, 0, playdia1, 0, 2048);
                            }

                            sense = dev.ReadCd(out cmdBuf, out senseBuf, 4201, 2324, 1, MmcSectorTypes.Mode2, false,
                                false, true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                MmcSubchannel.None, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                playdia2 = new byte[2048];
                                Array.Copy(cmdBuf, 0, playdia2, 0, 2048);
                            }

                            if (startOfFirstDataTrack != uint.MaxValue)
                            {
                                sense = dev.ReadCd(out cmdBuf, out senseBuf, startOfFirstDataTrack, 2324, 1,
                                    MmcSectorTypes.Mode2, false, false, true, MmcHeaderCodes.None, true,
                                    true, MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                                if (!sense && !dev.Error)
                                {
                                    firstDataSectorNotZero = new byte[2048];
                                    Array.Copy(cmdBuf, 0, firstDataSectorNotZero, 0, 2048);
                                }

                                sense = dev.ReadCd(out cmdBuf, out senseBuf, startOfFirstDataTrack + 1, 2324, 1,
                                    MmcSectorTypes.Mode2, false, false, true, MmcHeaderCodes.None, true,
                                    true, MmcErrorField.None, MmcSubchannel.None, dev.Timeout, out _);

                                if (!sense && !dev.Error)
                                {
                                    secondDataSectorNotZero = new byte[2048];
                                    Array.Copy(cmdBuf, 0, secondDataSectorNotZero, 0, 2048);
                                }
                            }

                            var ps2Ms = new MemoryStream();
                            for (uint p = 0; p < 12; p++)
                            {
                                sense = dev.ReadCd(out cmdBuf, out senseBuf, p, 2324, 1, MmcSectorTypes.Mode2, false,
                                    false, true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                    MmcSubchannel.None, dev.Timeout, out _);

                                if (sense || dev.Error) break;

                                ps2Ms.Write(cmdBuf, 0, 2048);
                            }

                            if (ps2Ms.Length == 0x6000) ps2BootSectors = ps2Ms.ToArray();
                        }
                        else
                        {
                            sense = dev.ReadCd(out cmdBuf, out senseBuf, 0, 2048, 1, MmcSectorTypes.Mode1, false, false,
                                true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                MmcSubchannel.None, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                sector0 = cmdBuf;

                                sense = dev.ReadCd(out cmdBuf, out senseBuf, 0, 2048, 1, MmcSectorTypes.Mode1, false,
                                    false, true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                    MmcSubchannel.None, dev.Timeout, out _);

                                if (!sense && !dev.Error) sector1 = cmdBuf;

                                sense = dev.ReadCd(out cmdBuf, out senseBuf, 0, 2048, 12, MmcSectorTypes.Mode1, false,
                                    false, true, MmcHeaderCodes.None, true, true, MmcErrorField.None,
                                    MmcSubchannel.None, dev.Timeout, out _);

                                if (!sense && !dev.Error) ps2BootSectors = cmdBuf;

                                if (startOfFirstDataTrack != uint.MaxValue)
                                {
                                    sense = dev.ReadCd(out cmdBuf, out senseBuf, startOfFirstDataTrack, 2048, 1,
                                        MmcSectorTypes.Mode1, false, false, true, MmcHeaderCodes.None,
                                        true, true, MmcErrorField.None, MmcSubchannel.None, dev.Timeout,
                                        out _);

                                    if (!sense && !dev.Error) firstDataSectorNotZero = cmdBuf;

                                    sense = dev.ReadCd(out cmdBuf, out senseBuf, startOfFirstDataTrack + 1, 2048, 1,
                                        MmcSectorTypes.Mode1, false, false, true, MmcHeaderCodes.None,
                                        true, true, MmcErrorField.None, MmcSubchannel.None, dev.Timeout,
                                        out _);

                                    if (!sense && !dev.Error) secondDataSectorNotZero = cmdBuf;
                                }
                            }
                            else
                            {
                                goto case MediaType.DVDROM;
                            }
                        }
                    }

                    break;
                }

                // TODO: Check for CD-i Ready
                case MediaType.CDI: break;
                case MediaType.DVDROM:
                case MediaType.HDDVDROM:
                case MediaType.BDROM:
                case MediaType.Unknown:
                    sense = dev.Read16(out cmdBuf, out senseBuf, 0, false, true, false, 0, BlockSize, 0, 1, false,
                        dev.Timeout, out _);

                    if (!sense && !dev.Error)
                    {
                        sector0 = cmdBuf;

                        sense = dev.Read16(out cmdBuf, out senseBuf, 0, false, true, false, 1, BlockSize, 0, 1, false,
                            dev.Timeout, out _);

                        if (!sense && !dev.Error) sector1 = cmdBuf;

                        sense = dev.Read16(out cmdBuf, out senseBuf, 0, false, true, false, 0, BlockSize, 0, 12, false,
                            dev.Timeout, out _);

                        if (!sense && !dev.Error && cmdBuf.Length == 0x6000) ps2BootSectors = cmdBuf;
                    }
                    else
                    {
                        sense = dev.Read12(out cmdBuf, out senseBuf, 0, false, true, false, false, 0, BlockSize, 0, 1,
                            false, dev.Timeout, out _);

                        if (!sense && !dev.Error)
                        {
                            sector0 = cmdBuf;

                            sense = dev.Read12(out cmdBuf, out senseBuf, 0, false, true, false, false, 1, BlockSize, 0,
                                1, false, dev.Timeout, out _);

                            if (!sense && !dev.Error) sector1 = cmdBuf;

                            sense = dev.Read12(out cmdBuf, out senseBuf, 0, false, true, false, false, 0, BlockSize, 0,
                                12, false, dev.Timeout, out _);

                            if (!sense && !dev.Error && cmdBuf.Length == 0x6000) ps2BootSectors = cmdBuf;
                        }
                        else
                        {
                            sense = dev.Read10(out cmdBuf, out senseBuf, 0, false, true, false, false, 0, BlockSize, 0,
                                1, dev.Timeout, out _);

                            if (!sense && !dev.Error)
                            {
                                sector0 = cmdBuf;

                                sense = dev.Read10(out cmdBuf, out senseBuf, 0, false, true, false, false, 1, BlockSize,
                                    0, 1, dev.Timeout, out _);

                                if (!sense && !dev.Error) sector1 = cmdBuf;

                                sense = dev.Read10(out cmdBuf, out senseBuf, 0, false, true, false, false, 0, BlockSize,
                                    0, 12, dev.Timeout, out _);

                                if (!sense && !dev.Error && cmdBuf.Length == 0x6000) ps2BootSectors = cmdBuf;
                            }
                            else
                            {
                                sense = dev.Read6(out cmdBuf, out senseBuf, 0, BlockSize, 1, dev.Timeout, out _);

                                if (!sense && !dev.Error)
                                {
                                    sector0 = cmdBuf;

                                    sense = dev.Read6(out cmdBuf, out senseBuf, 1, BlockSize, 1, dev.Timeout, out _);

                                    if (!sense && !dev.Error) sector1 = cmdBuf;

                                    sense = dev.Read6(out cmdBuf, out senseBuf, 0, BlockSize, 12, dev.Timeout, out _);

                                    if (!sense && !dev.Error && cmdBuf.Length == 0x6000) ps2BootSectors = cmdBuf;
                                }
                            }
                        }
                    }

                    break;
                // Recordables will not be checked
                case MediaType.CDR:
                case MediaType.CDRW:
                case MediaType.CDMRW:
                case MediaType.DDCDR:
                case MediaType.DDCDRW:
                case MediaType.DVDR:
                case MediaType.DVDRW:
                case MediaType.DVDPR:
                case MediaType.DVDPRW:
                case MediaType.DVDPRWDL:
                case MediaType.DVDRDL:
                case MediaType.DVDPRDL:
                case MediaType.DVDRAM:
                case MediaType.DVDRWDL:
                case MediaType.DVDDownload:
                case MediaType.HDDVDRAM:
                case MediaType.HDDVDR:
                case MediaType.HDDVDRW:
                case MediaType.HDDVDRDL:
                case MediaType.HDDVDRWDL:
                case MediaType.BDR:
                case MediaType.BDRE:
                case MediaType.BDRXL:
                case MediaType.BDREXL: return;
            }

            if (sector0 == null) return;

            switch (MediaType)
            {
                case MediaType.CD:
                case MediaType.CDDA:
                case MediaType.CDPLUS:
                case MediaType.CDROM:
                case MediaType.CDROMXA:
                    // TODO: CDTV requires reading the filesystem, searching for a file called "/CDTV.TM"
                    // TODO: CD32 requires reading the filesystem, searching for a file called "/CD32.TM"
                    // TODO: Neo-Geo CD requires reading the filesystem and checking that the file "/IPL.TXT" is correct
                    // TODO: Pippin requires interpreting Apple Partition Map, reading HFS and checking for Pippin signatures
                {
                    if (CD.DecodeIPBin(sector0).HasValue)
                    {
                        MediaType = MediaType.MEGACD;
                        return;
                    }

                    if (Saturn.DecodeIPBin(sector0).HasValue) MediaType = MediaType.SATURNCD;

                    // Are GDR detectable ???
                    if (Dreamcast.DecodeIPBin(sector0).HasValue) MediaType = MediaType.GDROM;

                    if (ps2BootSectors != null && ps2BootSectors.Length == 0x6000)
                    {
                        // The decryption key is applied as XOR. As first byte is originally always NULL, it gives us the key :)
                        var decryptByte = ps2BootSectors[0];
                        for (var i = 0; i < 0x6000; i++) ps2BootSectors[i] ^= decryptByte;

                        var ps2BootSectorsHash = Sha256Context.Data(ps2BootSectors, out _);
                        DicConsole.DebugWriteLine("Media-info Command", "PlayStation 2 boot sectors SHA256: {0}",
                            ps2BootSectorsHash);
                        if (ps2BootSectorsHash == PS2_PAL_HASH || ps2BootSectorsHash == PS2_NTSC_HASH ||
                            ps2BootSectorsHash == PS2_JAPANESE_HASH) MediaType = MediaType.PS2CD;
                    }

                    if (sector0 != null)
                    {
                        var syncBytes = new byte[7];
                        Array.Copy(sector0, 0, syncBytes, 0, 7);

                        if (OperaId.SequenceEqual(syncBytes)) MediaType = MediaType.ThreeDO;
                        if (FmTownsBootId.SequenceEqual(syncBytes)) MediaType = MediaType.FMTOWNS;
                    }

                    if (playdia1 != null && playdia2 != null)
                    {
                        var pd1 = new byte[PlaydiaCopyright.Length];
                        var pd2 = new byte[PlaydiaCopyright.Length];

                        Array.Copy(playdia1, 38, pd1, 0, pd1.Length);
                        Array.Copy(playdia2, 0, pd2, 0, pd1.Length);

                        if (PlaydiaCopyright.SequenceEqual(pd1) && PlaydiaCopyright.SequenceEqual(pd2))
                            MediaType = MediaType.Playdia;
                    }

                    if (secondDataSectorNotZero != null)
                    {
                        var pce = new byte[PcEngineSignature.Length];
                        Array.Copy(secondDataSectorNotZero, 32, pce, 0, pce.Length);

                        if (PcEngineSignature.SequenceEqual(pce)) MediaType = MediaType.SuperCDROM2;
                    }

                    if (firstDataSectorNotZero != null)
                    {
                        var pcfx = new byte[PcFxSignature.Length];
                        Array.Copy(firstDataSectorNotZero, 0, pcfx, 0, pcfx.Length);

                        if (PcFxSignature.SequenceEqual(pcfx)) MediaType = MediaType.PCFX;
                    }

                    if (firstTrackSecondSessionAudio != null)
                    {
                        var jaguar = new byte[AtariSignature.Length];
                        for (var i = 0; i + jaguar.Length <= firstTrackSecondSessionAudio.Length; i += 2)
                        {
                            Array.Copy(firstTrackSecondSessionAudio, i, jaguar, 0, jaguar.Length);

                            if (!AtariSignature.SequenceEqual(jaguar)) continue;

                            MediaType = MediaType.JaguarCD;
                            break;
                        }
                    }

                    if (firstTrackSecondSession != null)
                        if (firstTrackSecondSession.Length >= 2336)
                        {
                            var milcd = new byte[2048];
                            Array.Copy(firstTrackSecondSession, 24, milcd, 0, 2048);
                            if (Dreamcast.DecodeIPBin(milcd).HasValue) MediaType = MediaType.MilCD;
                        }

                    // TODO: Detect black and white VideoNow
                    // TODO: Detect VideoNow XP
                    if (MMC.IsVideoNowColor(videoNowColorFrame)) MediaType = MediaType.VideoNowColor;

                    break;
                }

                // TODO: Check for CD-i Ready
                case MediaType.CDI: break;
                case MediaType.DVDROM:
                case MediaType.HDDVDROM:
                case MediaType.BDROM:
                case MediaType.Unknown:
                    // TODO: Nuon requires reading the filesystem, searching for a file called "/NUON/NUON.RUN"
                    if (ps2BootSectors != null && ps2BootSectors.Length == 0x6000)
                    {
                        // The decryption key is applied as XOR. As first byte is originally always NULL, it gives us the key :)
                        var decryptByte = ps2BootSectors[0];
                        for (var i = 0; i < 0x6000; i++) ps2BootSectors[i] ^= decryptByte;

                        var ps2BootSectorsHash = Sha256Context.Data(ps2BootSectors, out _);
                        DicConsole.DebugWriteLine("Media-info Command", "PlayStation 2 boot sectors SHA256: {0}",
                            ps2BootSectorsHash);
                        if (ps2BootSectorsHash == PS2_PAL_HASH || ps2BootSectorsHash == PS2_NTSC_HASH ||
                            ps2BootSectorsHash == PS2_JAPANESE_HASH) MediaType = MediaType.PS2DVD;
                    }

                    if (sector1 != null)
                    {
                        var tmp = new byte[Ps3Id.Length];
                        Array.Copy(sector1, 0, tmp, 0, tmp.Length);
                        if (tmp.SequenceEqual(Ps3Id))
                            switch (MediaType)
                            {
                                case MediaType.BDROM:
                                    MediaType = MediaType.PS3BD;
                                    break;
                                case MediaType.DVDROM:
                                    MediaType = MediaType.PS3DVD;
                                    break;
                            }

                        tmp = new byte[Ps4Id.Length];
                        Array.Copy(sector1, 512, tmp, 0, tmp.Length);
                        if (tmp.SequenceEqual(Ps4Id) && MediaType == MediaType.BDROM) MediaType = MediaType.PS4BD;
                    }

                    // TODO: Identify discs that require reading tracks (PC-FX, PlayStation, Sega, etc)
                    break;
            }
        }

        public byte[] MediaSerialNumber { get; }
        public byte[] XboxSecuritySector { get; }
        public SS.SecuritySector? DecodedXboxSecuritySector { get; }
        public XgdInfo XgdInfo { get; }
        public byte[] MmcConfiguration { get; }
        public byte[] RecognizedFormatLayers { get; }
        public byte[] WriteProtectionStatus { get; }
        public byte[] DvdPfi { get; }
        public PFI.PhysicalFormatInformation? DecodedPfi { get; }
        public byte[] DvdDmi { get; }
        public byte[] DvdCmi { get; }
        public byte[] DvdBca { get; }
        public byte[] DvdAacs { get; }
        public byte[] DvdRamDds { get; }
        public byte[] DvdRamCartridgeStatus { get; }
        public byte[] DvdRamSpareArea { get; }
        public byte[] LastBorderOutRmd { get; }
        public byte[] DvdPreRecordedInfo { get; }
        public byte[] DvdrMediaIdentifier { get; }
        public byte[] DvdrPhysicalInformation { get; }
        public byte[] DvdPlusAdip { get; }
        public byte[] DvdPlusDcb { get; }
        public byte[] HddvdCopyrightInformation { get; }
        public byte[] HddvdrMediumStatus { get; }
        public byte[] HddvdrLastRmd { get; }
        public byte[] DvdrLayerCapacity { get; }
        public byte[] DvdrDlMiddleZoneStart { get; }
        public byte[] DvdrDlJumpIntervalSize { get; }
        public byte[] DvdrDlManualLayerJumpStartLba { get; }
        public byte[] DvdrDlRemapAnchorPoint { get; }
        public byte[] BlurayDiscInformation { get; }
        public byte[] BlurayPac { get; }
        public byte[] BlurayBurstCuttingArea { get; }
        public byte[] BlurayDds { get; }
        public byte[] BlurayCartridgeStatus { get; }
        public byte[] BluraySpareAreaInformation { get; }
        public byte[] BlurayRawDfl { get; }
        public byte[] BlurayPowResources { get; }
        public byte[] Toc { get; }
        public byte[] Atip { get; }
        public byte[] CompactDiscInformation { get; }
        public byte[] Session { get; }
        public byte[] RawToc { get; }
        public byte[] Pma { get; }
        public byte[] CdTextLeadIn { get; }
        public TOC.CDTOC? DecodedToc { get; }
        public ATIP.CDATIP? DecodedAtip { get; }
        public Session.CDSessionInfo? DecodedSession { get; }
        public FullTOC.CDFullTOC? FullToc { get; }
        public CDTextOnLeadIn.CDText? DecodedCdTextLeadIn { get; }
        public byte[] BlurayTrackResources { get; }
        public DiscInformation.StandardDiscInformation? DecodedCompactDiscInformation { get; }
        public string Mcn { get; }
        public Dictionary<byte, string> Isrcs { get; }
        public bool MediaInserted { get; }
        public MediaType MediaType { get; }
        public DeviceInfo DeviceInfo { get; }
        public byte[] ReadCapacity { get; }
        public ulong Blocks { get; }
        public uint BlockSize { get; }
        public byte[] ReadCapacity16 { get; }
        public byte[] DensitySupport { get; }
        public DensitySupport.DensitySupportHeader? DensitySupportHeader { get; }
        public byte[] MediaTypeSupport { get; }
        public DensitySupport.MediaTypeSupportHeader? MediaTypeSupportHeader { get; }
    }
}