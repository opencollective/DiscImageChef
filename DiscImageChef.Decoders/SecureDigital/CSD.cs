// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : CSD.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Device structures decoders.
//
// --[ Description ] ----------------------------------------------------------
//
//     Decodes MultiMediaCard CSD.
//
// --[ License ] --------------------------------------------------------------
//
//     This library is free software; you can redistribute it and/or modify
//     it under the terms of the GNU Lesser General Public License as
//     published by the Free Software Foundation; either version 2.1 of the
//     License, or (at your option) any later version.
//
//     This library is distributed in the hope that it will be useful, but
//     WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//     Lesser General Public License for more details.
//
//     You should have received a copy of the GNU Lesser General Public
//     License along with this library; if not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright © 2011-2019 Natalia Portillo
// ****************************************************************************/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DiscImageChef.Decoders.SecureDigital
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "MemberCanBeInternal")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class CSD
    {
        public byte   Structure;
        public byte   TAAC;
        public byte   NSAC;
        public byte   Speed;
        public ushort Classes;
        public byte   ReadBlockLength;
        public bool   ReadsPartialBlocks;
        public bool   WriteMisalignment;
        public bool   ReadMisalignment;
        public bool   DSRImplemented;
        public uint   Size;
        public byte   ReadCurrentAtVddMin;
        public byte   ReadCurrentAtVddMax;
        public byte   WriteCurrentAtVddMin;
        public byte   WriteCurrentAtVddMax;
        public byte   SizeMultiplier;
        public bool   EraseBlockEnable;
        public byte   EraseSectorSize;
        public byte   WriteProtectGroupSize;
        public bool   WriteProtectGroupEnable;
        public byte   WriteSpeedFactor;
        public byte   WriteBlockLength;
        public bool   WritesPartialBlocks;
        public bool   FileFormatGroup;
        public bool   Copy;
        public bool   PermanentWriteProtect;
        public bool   TemporaryWriteProtect;
        public byte   FileFormat;
        public byte   CRC;
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public static partial class Decoders
    {
        public static CSD DecodeCSD(uint[] response)
        {
            if(response?.Length != 4) return null;

            byte[] data = new byte[16];

            byte[] tmp = BitConverter.GetBytes(response[0]);
            Array.Copy(tmp, 0, data, 0, 4);
            tmp = BitConverter.GetBytes(response[1]);
            Array.Copy(tmp, 0, data, 4, 4);
            tmp = BitConverter.GetBytes(response[2]);
            Array.Copy(tmp, 0, data, 8, 4);
            tmp = BitConverter.GetBytes(response[3]);
            Array.Copy(tmp, 0, data, 12, 4);

            return DecodeCSD(data);
        }

        public static CSD DecodeCSD(byte[] response)
        {
            if(response?.Length != 16) return null;

            CSD csd = new CSD
            {
                Structure               = (byte)((response[0] & 0xC0) >> 6),
                TAAC                    = response[1],
                NSAC                    = response[2],
                Speed                   = response[3],
                Classes                 = (ushort)((response[4] << 4) + ((response[5] & 0xF0) >> 4)),
                ReadBlockLength         = (byte)(response[5] & 0x0F),
                ReadsPartialBlocks      = (response[6]       & 0x80) == 0x80,
                WriteMisalignment       = (response[6]       & 0x40) == 0x40,
                ReadMisalignment        = (response[6]       & 0x20) == 0x20,
                DSRImplemented          = (response[6]       & 0x10) == 0x10,
                EraseBlockEnable        = (response[10]      & 0x40) == 0x40,
                EraseSectorSize         = (byte)(((response[10] & 0x3F) << 1) + ((response[11] & 0x80) >> 7)),
                WriteProtectGroupSize   = (byte)(response[11] & 0x7F),
                WriteProtectGroupEnable = (response[12]       & 0x80) == 0x80,
                WriteSpeedFactor        = (byte)((response[12] & 0x1C) >> 2),
                WriteBlockLength        = (byte)(((response[12] & 0x03) << 2) + ((response[13] & 0xC0) >> 6)),
                WritesPartialBlocks     = (response[13] & 0x20) == 0x20,
                FileFormatGroup         = (response[14] & 0x80) == 0x80,
                Copy                    = (response[14] & 0x40) == 0x40,
                PermanentWriteProtect   = (response[14] & 0x20) == 0x20,
                TemporaryWriteProtect   = (response[14] & 0x10) == 0x10,
                FileFormat              = (byte)((response[14] & 0x0C) >> 2),
                CRC                     = (byte)((response[15] & 0xFE) >> 1)
            };

            if(csd.Structure == 0)
            {
                csd.Size =
                    (ushort)(((response[6] & 0x03) << 10) + (response[7] << 2) + ((response[8] & 0xC0) >> 6));
                csd.ReadCurrentAtVddMin  = (byte)((response[8] & 0x38) >> 3);
                csd.ReadCurrentAtVddMax  = (byte)(response[8] & 0x07);
                csd.WriteCurrentAtVddMin = (byte)((response[9] & 0xE0) >> 5);
                csd.WriteCurrentAtVddMax = (byte)((response[9] & 0x1C) >> 2);
                csd.SizeMultiplier       = (byte)(((response[9] & 0x03) << 1) + ((response[10] & 0x80) >> 7));
            }
            else csd.Size = (uint)(((response[7] & 0x3F) << 16) + (response[8] << 8) + response[9]);

            return csd;
        }

        public static string PrettifyCSD(CSD csd)
        {
            if(csd == null) return null;

            double unitFactor = 0;
            double multiplier = 0;
            string unit       = "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SecureDigital Device Specific Data Register:");
            switch(csd.Structure)
            {
                case 0:
                    sb.AppendLine("\tRegister version 1.0");
                    break;
                case 1:
                    sb.AppendLine("\tRegister version 2.0");
                    break;
            }

            switch(csd.TAAC & 0x07)
            {
                case 0:
                    unit       = "ns";
                    unitFactor = 1;
                    break;
                case 1:
                    unit       = "ns";
                    unitFactor = 10;
                    break;
                case 2:
                    unit       = "ns";
                    unitFactor = 100;
                    break;
                case 3:
                    unit       = "μs";
                    unitFactor = 1;
                    break;
                case 4:
                    unit       = "μs";
                    unitFactor = 10;
                    break;
                case 5:
                    unit       = "μs";
                    unitFactor = 100;
                    break;
                case 6:
                    unit       = "ms";
                    unitFactor = 1;
                    break;
                case 7:
                    unit       = "ms";
                    unitFactor = 10;
                    break;
            }

            switch((csd.TAAC & 0x78) >> 3)
            {
                case 0:
                    multiplier = 0;
                    break;
                case 1:
                    multiplier = 1;
                    break;
                case 2:
                    multiplier = 1.2;
                    break;
                case 3:
                    multiplier = 1.3;
                    break;
                case 4:
                    multiplier = 1.5;
                    break;
                case 5:
                    multiplier = 2;
                    break;
                case 6:
                    multiplier = 2.5;
                    break;
                case 7:
                    multiplier = 3;
                    break;
                case 8:
                    multiplier = 3.5;
                    break;
                case 9:
                    multiplier = 4;
                    break;
                case 10:
                    multiplier = 4.5;
                    break;
                case 11:
                    multiplier = 5;
                    break;
                case 12:
                    multiplier = 5.5;
                    break;
                case 13:
                    multiplier = 6;
                    break;
                case 14:
                    multiplier = 7;
                    break;
                case 15:
                    multiplier = 8;
                    break;
            }

            double result = unitFactor * multiplier;
            sb.AppendFormat("\tAsynchronous data access time is {0}{1}", result, unit).AppendLine();

            sb.AppendFormat("\tClock dependent part of data access is {0} clock cycles", csd.NSAC * 100).AppendLine();

            unit = "MBit/s";
            switch(csd.Speed & 0x07)
            {
                case 0:
                    unitFactor = 0.1;
                    break;
                case 1:
                    unitFactor = 1;
                    break;
                case 2:
                    unitFactor = 10;
                    break;
                case 3:
                    unitFactor = 100;
                    break;
                default:
                    unit       = "unknown";
                    unitFactor = 0;
                    break;
            }

            switch((csd.Speed & 0x78) >> 3)
            {
                case 0:
                    multiplier = 0;
                    break;
                case 1:
                    multiplier = 1;
                    break;
                case 2:
                    multiplier = 1.2;
                    break;
                case 3:
                    multiplier = 1.3;
                    break;
                case 4:
                    multiplier = 1.5;
                    break;
                case 5:
                    multiplier = 2;
                    break;
                case 6:
                    multiplier = 2.6;
                    break;
                case 7:
                    multiplier = 3;
                    break;
                case 8:
                    multiplier = 3.5;
                    break;
                case 9:
                    multiplier = 4;
                    break;
                case 10:
                    multiplier = 4.5;
                    break;
                case 11:
                    multiplier = 5.2;
                    break;
                case 12:
                    multiplier = 5.5;
                    break;
                case 13:
                    multiplier = 6;
                    break;
                case 14:
                    multiplier = 7;
                    break;
                case 15:
                    multiplier = 8;
                    break;
            }

            result = unitFactor * multiplier;
            sb.AppendFormat("\tDevice's transfer speed: {0}{1}", result, unit).AppendLine();

            unit = "";
            for(int cl = 0, mask = 1; cl <= 11; cl++, mask <<= 1)
                if((csd.Classes & mask) == mask)
                    unit += $" {cl}";

            sb.AppendFormat("\tDevice support command classes {0}", unit).AppendLine();
            sb.AppendFormat("\tRead block length is {0} bytes", Math.Pow(2, csd.ReadBlockLength)).AppendLine();

            if(csd.ReadsPartialBlocks) sb.AppendLine("\tDevice allows reading partial blocks");

            if(csd.WriteMisalignment) sb.AppendLine("\tWrite commands can cross physical block boundaries");
            if(csd.ReadMisalignment) sb.AppendLine("\tRead commands can cross physical block boundaries");

            if(csd.DSRImplemented) sb.AppendLine("\tDevice implements configurable driver stage");

            if(csd.Structure == 0)
            {
                result = (csd.Size + 1) * Math.Pow(2, csd.SizeMultiplier + 2);
                sb.AppendFormat("\tDevice has {0} blocks", (int)result).AppendLine();

                result = (csd.Size + 1) * Math.Pow(2, csd.SizeMultiplier + 2) * Math.Pow(2, csd.ReadBlockLength);
                if(result > 1073741824)
                    sb.AppendFormat("\tDevice has {0} GiB", result / 1073741824.0).AppendLine();
                else if(result > 1048576) sb.AppendFormat("\tDevice has {0} MiB", result / 1048576.0).AppendLine();
                else if(result > 1024) sb.AppendFormat("\tDevice has {0} KiB", result / 1024.0).AppendLine();
                else sb.AppendFormat("\tDevice has {0} bytes", result).AppendLine();
            }
            else
            {
                sb.AppendFormat("\tDevice has {0} blocks", (csd.Size + 1) * 1024).AppendLine();
                result = ((ulong)csd.Size + 1) * 1024 * 512;
                if(result > 1099511627776)
                    sb.AppendFormat("\tDevice has {0} TiB", result / 1099511627776.0).AppendLine();
                else if(result > 1073741824)
                    sb.AppendFormat("\tDevice has {0} GiB", result / 1073741824.0).AppendLine();
                else if(result > 1048576) sb.AppendFormat("\tDevice has {0} MiB", result / 1048576.0).AppendLine();
                else if(result > 1024) sb.AppendFormat("\tDevice has {0} KiB", result / 1024.0).AppendLine();
                else sb.AppendFormat("\tDevice has {0} bytes", result).AppendLine();
            }

            if(csd.Structure == 0)
            {
                switch(csd.ReadCurrentAtVddMin & 0x07)
                {
                    case 0:
                        sb.AppendLine("\tDevice uses a maximum of 0.5mA for reading at minimum voltage");
                        break;
                    case 1:
                        sb.AppendLine("\tDevice uses a maximum of 1mA for reading at minimum voltage");
                        break;
                    case 2:
                        sb.AppendLine("\tDevice uses a maximum of 5mA for reading at minimum voltage");
                        break;
                    case 3:
                        sb.AppendLine("\tDevice uses a maximum of 10mA for reading at minimum voltage");
                        break;
                    case 4:
                        sb.AppendLine("\tDevice uses a maximum of 25mA for reading at minimum voltage");
                        break;
                    case 5:
                        sb.AppendLine("\tDevice uses a maximum of 35mA for reading at minimum voltage");
                        break;
                    case 6:
                        sb.AppendLine("\tDevice uses a maximum of 60mA for reading at minimum voltage");
                        break;
                    case 7:
                        sb.AppendLine("\tDevice uses a maximum of 100mA for reading at minimum voltage");
                        break;
                }

                switch(csd.ReadCurrentAtVddMax & 0x07)
                {
                    case 0:
                        sb.AppendLine("\tDevice uses a maximum of 1mA for reading at maximum voltage");
                        break;
                    case 1:
                        sb.AppendLine("\tDevice uses a maximum of 5mA for reading at maximum voltage");
                        break;
                    case 2:
                        sb.AppendLine("\tDevice uses a maximum of 10mA for reading at maximum voltage");
                        break;
                    case 3:
                        sb.AppendLine("\tDevice uses a maximum of 25mA for reading at maximum voltage");
                        break;
                    case 4:
                        sb.AppendLine("\tDevice uses a maximum of 35mA for reading at maximum voltage");
                        break;
                    case 5:
                        sb.AppendLine("\tDevice uses a maximum of 45mA for reading at maximum voltage");
                        break;
                    case 6:
                        sb.AppendLine("\tDevice uses a maximum of 80mA for reading at maximum voltage");
                        break;
                    case 7:
                        sb.AppendLine("\tDevice uses a maximum of 200mA for reading at maximum voltage");
                        break;
                }

                switch(csd.WriteCurrentAtVddMin & 0x07)
                {
                    case 0:
                        sb.AppendLine("\tDevice uses a maximum of 0.5mA for writing at minimum voltage");
                        break;
                    case 1:
                        sb.AppendLine("\tDevice uses a maximum of 1mA for writing at minimum voltage");
                        break;
                    case 2:
                        sb.AppendLine("\tDevice uses a maximum of 5mA for writing at minimum voltage");
                        break;
                    case 3:
                        sb.AppendLine("\tDevice uses a maximum of 10mA for writing at minimum voltage");
                        break;
                    case 4:
                        sb.AppendLine("\tDevice uses a maximum of 25mA for writing at minimum voltage");
                        break;
                    case 5:
                        sb.AppendLine("\tDevice uses a maximum of 35mA for writing at minimum voltage");
                        break;
                    case 6:
                        sb.AppendLine("\tDevice uses a maximum of 60mA for writing at minimum voltage");
                        break;
                    case 7:
                        sb.AppendLine("\tDevice uses a maximum of 100mA for writing at minimum voltage");
                        break;
                }

                switch(csd.WriteCurrentAtVddMax & 0x07)
                {
                    case 0:
                        sb.AppendLine("\tDevice uses a maximum of 1mA for writing at maximum voltage");
                        break;
                    case 1:
                        sb.AppendLine("\tDevice uses a maximum of 5mA for writing at maximum voltage");
                        break;
                    case 2:
                        sb.AppendLine("\tDevice uses a maximum of 10mA for writing at maximum voltage");
                        break;
                    case 3:
                        sb.AppendLine("\tDevice uses a maximum of 25mA for writing at maximum voltage");
                        break;
                    case 4:
                        sb.AppendLine("\tDevice uses a maximum of 35mA for writing at maximum voltage");
                        break;
                    case 5:
                        sb.AppendLine("\tDevice uses a maximum of 45mA for writing at maximum voltage");
                        break;
                    case 6:
                        sb.AppendLine("\tDevice uses a maximum of 80mA for writing at maximum voltage");
                        break;
                    case 7:
                        sb.AppendLine("\tDevice uses a maximum of 200mA for writing at maximum voltage");
                        break;
                }

                if(csd.EraseBlockEnable) sb.AppendLine("\tDevice can erase multiple blocks");

                // TODO: Check specification
                sb.AppendFormat("\tDevice must erase a minimum of {0} blocks at a time",
                                Convert.ToUInt32(csd.EraseSectorSize) + 1).AppendLine();

                if(csd.WriteProtectGroupEnable)
                {
                    sb.AppendLine("\tDevice can write protect regions");
                    // TODO: Check specification
                    unitFactor = Convert.ToDouble(csd.WriteProtectGroupSize);
                    sb.AppendFormat("\tDevice can write protect a minimum of {0} blocks at a time", (int)(result + 1))
                      .AppendLine();
                }
                else sb.AppendLine("\tDevice can't write protect regions");
            }

            sb.AppendFormat("\tWriting is {0} times slower than reading", Math.Pow(2, csd.WriteSpeedFactor))
              .AppendLine();

            sb.AppendFormat("\tWrite block length is {0} bytes", Math.Pow(2, csd.WriteBlockLength)).AppendLine();

            if(csd.WritesPartialBlocks) sb.AppendLine("\tDevice allows writing partial blocks");

            if(!csd.Copy) sb.AppendLine("\tDevice contents are original");

            if(csd.PermanentWriteProtect) sb.AppendLine("\tDevice is permanently write protected");

            if(csd.TemporaryWriteProtect) sb.AppendLine("\tDevice is temporarily write protected");

            if(!csd.FileFormatGroup)
                switch(csd.FileFormat)
                {
                    case 0:
                        sb.AppendLine("\tDevice is formatted like a hard disk");
                        break;
                    case 1:
                        sb.AppendLine("\tDevice is formatted like a floppy disk using Microsoft FAT");
                        break;
                    case 2:
                        sb.AppendLine("\tDevice uses Universal File Format");
                        break;
                    default:
                        sb.AppendFormat("\tDevice uses unknown file format code {0}", csd.FileFormat).AppendLine();
                        break;
                }
            else
                sb.AppendFormat("\tDevice uses unknown file format code {0} and file format group 1", csd.FileFormat)
                  .AppendLine();

            sb.AppendFormat("\tCSD CRC: 0x{0:X2}", csd.CRC).AppendLine();

            return sb.ToString();
        }

        public static string PrettifyCSD(uint[] response) => PrettifyCSD(DecodeCSD(response));

        public static string PrettifyCSD(byte[] response) => PrettifyCSD(DecodeCSD(response));
    }
}