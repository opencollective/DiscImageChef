// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Verify.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Disk image plugins.
//
// --[ Description ] ----------------------------------------------------------
//
//     Verifies MAME Compressed Hunks of Data disk images.
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
using System.Collections.Generic;
using System.Linq;
using DiscImageChef.Checksums;
using DiscImageChef.CommonTypes.Exceptions;

namespace DiscImageChef.DiscImages
{
    public partial class Chd
    {
        public bool? VerifySector(ulong sectorAddress)
        {
            if(isHdd) return null;

            byte[] buffer = ReadSectorLong(sectorAddress);
            return CdChecksums.CheckCdSector(buffer);
        }

        public bool? VerifySectors(ulong           sectorAddress, uint length, out List<ulong> failingLbas,
                                   out List<ulong> unknownLbas)
        {
            unknownLbas = new List<ulong>();
            failingLbas = new List<ulong>();
            if(isHdd) return null;

            byte[] buffer = ReadSectorsLong(sectorAddress, length);
            int    bps    = (int)(buffer.Length / length);
            byte[] sector = new byte[bps];

            for(int i = 0; i < length; i++)
            {
                Array.Copy(buffer, i * bps, sector, 0, bps);
                bool? sectorStatus = CdChecksums.CheckCdSector(sector);

                switch(sectorStatus)
                {
                    case null:
                        unknownLbas.Add((ulong)i + sectorAddress);
                        break;
                    case false:
                        failingLbas.Add((ulong)i + sectorAddress);
                        break;
                }
            }

            if(unknownLbas.Count > 0) return null;

            return failingLbas.Count <= 0;
        }

        public bool? VerifySectors(ulong           sectorAddress, uint length, uint track, out List<ulong> failingLbas,
                                   out List<ulong> unknownLbas)
        {
            unknownLbas = new List<ulong>();
            failingLbas = new List<ulong>();
            if(isHdd) return null;

            byte[] buffer = ReadSectorsLong(sectorAddress, length, track);
            int    bps    = (int)(buffer.Length / length);
            byte[] sector = new byte[bps];

            for(int i = 0; i < length; i++)
            {
                Array.Copy(buffer, i * bps, sector, 0, bps);
                bool? sectorStatus = CdChecksums.CheckCdSector(sector);

                switch(sectorStatus)
                {
                    case null:
                        unknownLbas.Add((ulong)i + sectorAddress);
                        break;
                    case false:
                        failingLbas.Add((ulong)i + sectorAddress);
                        break;
                }
            }

            if(unknownLbas.Count > 0) return null;

            return failingLbas.Count <= 0;
        }

        public bool? VerifyMediaImage()
        {
            byte[] calculated;
            if(mapVersion >= 3)
            {
                Sha1Context sha1Ctx = new Sha1Context();
                for(uint i = 0; i < totalHunks; i++) sha1Ctx.Update(GetHunk(i));

                calculated = sha1Ctx.Final();
            }
            else
            {
                Md5Context md5Ctx = new Md5Context();
                for(uint i = 0; i < totalHunks; i++) md5Ctx.Update(GetHunk(i));

                calculated = md5Ctx.Final();
            }

            return expectedChecksum.SequenceEqual(calculated);
        }

        public bool? VerifySector(ulong sectorAddress, uint track)
        {
            if(isHdd)
                throw new FeaturedNotSupportedByDiscImageException("Cannot access optical tracks on a hard disk image");

            return VerifySector(GetAbsoluteSector(sectorAddress, track));
        }
    }
}