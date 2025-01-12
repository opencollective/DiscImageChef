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
//     Verifies CDRWin format disc images.
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
using System.IO;
using System.Linq;
using DiscImageChef.Checksums;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.Console;

namespace DiscImageChef.DiscImages
{
    public partial class CdrWin
    {
        public bool? VerifyMediaImage()
        {
            if(discimage.DiscHashes.Count == 0) return null;

            // Read up to 1MiB at a time for verification
            const int VERIFY_SIZE = 1024 * 1024;
            long      readBytes;
            byte[]    verifyBytes;

            IFilter[] filters = discimage.Tracks.OrderBy(t => t.Sequence).Select(t => t.Trackfile.Datafilter).Distinct()
                                         .ToArray();

            if(discimage.DiscHashes.TryGetValue("sha1", out string sha1))
            {
                Sha1Context ctx = new Sha1Context();

                foreach(IFilter filter in filters)
                {
                    Stream stream = filter.GetDataForkStream();
                    readBytes   = 0;
                    verifyBytes = new byte[VERIFY_SIZE];

                    while(readBytes + VERIFY_SIZE < stream.Length)
                    {
                        stream.Read(verifyBytes, 0, verifyBytes.Length);
                        ctx.Update(verifyBytes);
                        readBytes += verifyBytes.LongLength;
                    }

                    verifyBytes = new byte[stream.Length - readBytes];
                    stream.Read(verifyBytes, 0, verifyBytes.Length);
                    ctx.Update(verifyBytes);
                }

                string verifySha1 = ctx.End();
                DicConsole.DebugWriteLine("CDRWin plugin", "Calculated SHA1: {0}", verifySha1);
                DicConsole.DebugWriteLine("CDRWin plugin", "Expected SHA1: {0}",   sha1);

                return verifySha1 == sha1;
            }

            if(discimage.DiscHashes.TryGetValue("md5", out string md5))
            {
                Md5Context ctx = new Md5Context();

                foreach(IFilter filter in filters)
                {
                    Stream stream = filter.GetDataForkStream();
                    readBytes   = 0;
                    verifyBytes = new byte[VERIFY_SIZE];

                    while(readBytes + VERIFY_SIZE < stream.Length)
                    {
                        stream.Read(verifyBytes, 0, verifyBytes.Length);
                        ctx.Update(verifyBytes);
                        readBytes += verifyBytes.LongLength;
                    }

                    verifyBytes = new byte[stream.Length - readBytes];
                    stream.Read(verifyBytes, 0, verifyBytes.Length);
                    ctx.Update(verifyBytes);
                }

                string verifyMd5 = ctx.End();
                DicConsole.DebugWriteLine("CDRWin plugin", "Calculated MD5: {0}", verifyMd5);
                DicConsole.DebugWriteLine("CDRWin plugin", "Expected MD5: {0}",   md5);

                return verifyMd5 == md5;
            }

            if(discimage.DiscHashes.TryGetValue("crc32", out string crc32))
            {
                Crc32Context ctx = new Crc32Context();

                foreach(IFilter filter in filters)
                {
                    Stream stream = filter.GetDataForkStream();
                    readBytes   = 0;
                    verifyBytes = new byte[VERIFY_SIZE];

                    while(readBytes + VERIFY_SIZE < stream.Length)
                    {
                        stream.Read(verifyBytes, 0, verifyBytes.Length);
                        ctx.Update(verifyBytes);
                        readBytes += verifyBytes.LongLength;
                    }

                    verifyBytes = new byte[stream.Length - readBytes];
                    stream.Read(verifyBytes, 0, verifyBytes.Length);
                    ctx.Update(verifyBytes);
                }

                string verifyCrc = ctx.End();
                DicConsole.DebugWriteLine("CDRWin plugin", "Calculated CRC32: {0}", verifyCrc);
                DicConsole.DebugWriteLine("CDRWin plugin", "Expected CRC32: {0}",   crc32);

                return verifyCrc == crc32;
            }

            foreach(string hash in discimage.DiscHashes.Keys)
                DicConsole.DebugWriteLine("CDRWin plugin", "Found unsupported hash {0}", hash);

            return null;
        }

        public bool? VerifySector(ulong sectorAddress)
        {
            byte[] buffer = ReadSectorLong(sectorAddress);
            return CdChecksums.CheckCdSector(buffer);
        }

        public bool? VerifySectors(ulong           sectorAddress, uint length, out List<ulong> failingLbas,
                                   out List<ulong> unknownLbas)
        {
            byte[] buffer = ReadSectorsLong(sectorAddress, length);
            int    bps    = (int)(buffer.Length / length);
            byte[] sector = new byte[bps];
            failingLbas = new List<ulong>();
            unknownLbas = new List<ulong>();

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
            byte[] buffer = ReadSectorsLong(sectorAddress, length, track);
            int    bps    = (int)(buffer.Length / length);
            byte[] sector = new byte[bps];
            failingLbas = new List<ulong>();
            unknownLbas = new List<ulong>();

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

        public bool? VerifySector(ulong sectorAddress, uint track)
        {
            byte[] buffer = ReadSectorLong(sectorAddress, track);
            return CdChecksums.CheckCdSector(buffer);
        }
    }
}