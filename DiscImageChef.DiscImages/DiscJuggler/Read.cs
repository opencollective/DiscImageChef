﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Read.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Disk image plugins.
//
// --[ Description ] ----------------------------------------------------------
//
//     Reads DiscJuggler disc images.
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
using System.Text;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Exceptions;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.CommonTypes.Structs;
using DiscImageChef.Console;

namespace DiscImageChef.DiscImages
{
    public partial class DiscJuggler
    {
        public bool Open(IFilter imageFilter)
        {
            imageStream = imageFilter.GetDataForkStream();

            imageStream.Seek(-4, SeekOrigin.End);
            byte[] dscLenB = new byte[4];
            imageStream.Read(dscLenB, 0, 4);
            int dscLen = BitConverter.ToInt32(dscLenB, 0);

            if(dscLen >= imageStream.Length) return false;

            byte[] descriptor = new byte[dscLen];
            imageStream.Seek(-dscLen, SeekOrigin.End);
            imageStream.Read(descriptor, 0, dscLen);

            // Sessions
            if(descriptor[0] > 99 || descriptor[0] == 0) return false;

            int position = 1;

            ushort sessionSequence = 0;
            Sessions   = new List<Session>();
            Tracks     = new List<Track>();
            Partitions = new List<Partition>();
            offsetmap  = new Dictionary<uint, ulong>();
            trackFlags = new Dictionary<uint, byte>();
            ushort mediumType;
            byte   maxS = descriptor[0];

            DicConsole.DebugWriteLine("DiscJuggler plugin", "maxS = {0}", maxS);
            uint  lastSessionTrack = 0;
            ulong currentOffset    = 0;

            // Read sessions
            for(byte s = 0; s <= maxS; s++)
            {
                DicConsole.DebugWriteLine("DiscJuggler plugin", "s = {0}", s);

                // Seems all sessions start with this data
                if(descriptor[position + 0]  != 0x00 || descriptor[position + 2]  != 0x00 ||
                   descriptor[position + 3]  != 0x00 || descriptor[position + 4]  != 0x00 ||
                   descriptor[position + 5]  != 0x00 || descriptor[position + 6]  != 0x00 ||
                   descriptor[position + 7]  != 0x00 || descriptor[position + 8]  != 0x00 ||
                   descriptor[position + 9]  != 0x01 || descriptor[position + 10] != 0x00 ||
                   descriptor[position + 11] != 0x00 || descriptor[position + 12] != 0x00 ||
                   descriptor[position + 13] != 0xFF || descriptor[position + 14] != 0xFF) return false;

                // Too many tracks
                if(descriptor[position + 1] > 99) return false;

                byte maxT = descriptor[position + 1];
                DicConsole.DebugWriteLine("DiscJuggler plugin", "maxT = {0}", maxT);

                sessionSequence++;
                Session session = new Session
                {
                    SessionSequence = sessionSequence, EndTrack = uint.MinValue, StartTrack = uint.MaxValue
                };

                position += 15;
                bool addedATrack = false;

                // Read track
                for(byte t = 0; t < maxT; t++)
                {
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "t = {0}", t);
                    Track track = new Track();

                    // Skip unknown
                    position += 16;

                    byte[] trackFilenameB = new byte[descriptor[position]];
                    position++;
                    Array.Copy(descriptor, position, trackFilenameB, 0, trackFilenameB.Length);
                    position        += trackFilenameB.Length;
                    track.TrackFile =  Path.GetFileName(Encoding.Default.GetString(trackFilenameB));
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tfilename = {0}", track.TrackFile);

                    // Skip unknown
                    position += 29;

                    mediumType =  BitConverter.ToUInt16(descriptor, position);
                    position   += 2;
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tmediumType = {0}", mediumType);

                    // Read indices
                    track.Indexes = new Dictionary<int, ulong>();
                    ushort maxI = BitConverter.ToUInt16(descriptor, position);
                    position += 2;
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tmaxI = {0}", maxI);
                    for(ushort i = 0; i < maxI; i++)
                    {
                        uint index = BitConverter.ToUInt32(descriptor, position);
                        track.Indexes.Add(i, index);
                        position += 4;
                        DicConsole.DebugWriteLine("DiscJuggler plugin", "\tindex[{1}] = {0}", index, i);
                    }

                    // Read CD-Text
                    uint maxC = BitConverter.ToUInt32(descriptor, position);
                    position += 4;
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tmaxC = {0}", maxC);
                    for(uint c = 0; c < maxC; c++)
                    {
                        for(int cb = 0; cb < 18; cb++)
                        {
                            int bLen = descriptor[position];
                            position++;
                            DicConsole.DebugWriteLine("DiscJuggler plugin", "\tc[{1}][{2}].Length = {0}", bLen, c, cb);
                            if(bLen <= 0) continue;

                            byte[] textBlk = new byte[bLen];
                            Array.Copy(descriptor, position, textBlk, 0, bLen);
                            position += bLen;
                            // Track title
                            if(cb != 10) continue;

                            track.TrackDescription = Encoding.Default.GetString(textBlk, 0, bLen);
                            DicConsole.DebugWriteLine("DiscJuggler plugin", "\tTrack title = {0}",
                                                      track.TrackDescription);
                        }
                    }

                    position += 2;
                    uint trackMode = BitConverter.ToUInt32(descriptor, position);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\ttrackMode = {0}", trackMode);
                    position += 4;

                    // Skip unknown
                    position += 4;

                    session.SessionSequence = (ushort)(BitConverter.ToUInt32(descriptor, position) + 1);
                    track.TrackSession      = (ushort)(session.SessionSequence                     + 1);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tsession = {0}", session.SessionSequence);
                    position            += 4;
                    track.TrackSequence =  BitConverter.ToUInt32(descriptor, position) + lastSessionTrack + 1;
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\ttrack = {1} + {2} + 1 = {0}",
                                              track.TrackSequence, BitConverter.ToUInt32(descriptor, position),
                                              lastSessionTrack);
                    position               += 4;
                    track.TrackStartSector =  BitConverter.ToUInt32(descriptor, position);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\ttrackStart = {0}", track.TrackStartSector);
                    position += 4;
                    uint trackLen = BitConverter.ToUInt32(descriptor, position);
                    track.TrackEndSector = track.TrackStartSector + trackLen - 1;
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\ttrackEnd = {0}", track.TrackEndSector);
                    position += 4;

                    if(track.TrackSequence > session.EndTrack)
                    {
                        session.EndTrack  = track.TrackSequence;
                        session.EndSector = track.TrackEndSector;
                    }

                    if(track.TrackSequence < session.StartTrack)
                    {
                        session.StartTrack  = track.TrackSequence;
                        session.StartSector = track.TrackStartSector;
                    }

                    // Skip unknown
                    position += 16;

                    uint readMode = BitConverter.ToUInt32(descriptor, position);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\treadMode = {0}", readMode);
                    position += 4;
                    uint trackCtl = BitConverter.ToUInt32(descriptor, position);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\ttrackCtl = {0}", trackCtl);
                    position += 4;

                    // Skip unknown
                    position += 9;

                    byte[] isrc = new byte[12];
                    Array.Copy(descriptor, position, isrc, 0, 12);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tisrc = {0}", StringHandlers.CToString(isrc));
                    position += 12;
                    uint isrcValid = BitConverter.ToUInt32(descriptor, position);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tisrc_valid = {0}", isrcValid);
                    position += 4;

                    // Skip unknown
                    position += 87;

                    byte sessionType = descriptor[position];
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tsessionType = {0}", sessionType);
                    position++;

                    // Skip unknown
                    position += 5;

                    byte trackFollows = descriptor[position];
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\ttrackFollows = {0}", trackFollows);
                    position += 2;

                    uint endAddress = BitConverter.ToUInt32(descriptor, position);
                    DicConsole.DebugWriteLine("DiscJuggler plugin", "\tendAddress = {0}", endAddress);
                    position += 4;

                    // As to skip the lead-in
                    bool firstTrack = currentOffset == 0;

                    track.TrackSubchannelType = TrackSubchannelType.None;

                    switch(trackMode)
                    {
                        // Audio
                        case 0:
                            if(imageInfo.SectorSize < 2352) imageInfo.SectorSize = 2352;
                            track.TrackType              = TrackType.Audio;
                            track.TrackBytesPerSector    = 2352;
                            track.TrackRawBytesPerSector = 2352;
                            switch(readMode)
                            {
                                case 2:
                                    if(firstTrack) currentOffset += 150 * (ulong)track.TrackRawBytesPerSector;
                                    track.TrackFileOffset =  currentOffset;
                                    currentOffset         += trackLen * (ulong)track.TrackRawBytesPerSector;
                                    break;
                                case 3:
                                    if(firstTrack) currentOffset += 150 * (ulong)(track.TrackRawBytesPerSector + 16);
                                    track.TrackFileOffset       = currentOffset;
                                    track.TrackSubchannelFile   = track.TrackFile;
                                    track.TrackSubchannelOffset = currentOffset;
                                    track.TrackSubchannelType   = TrackSubchannelType.Q16Interleaved;
                                    currentOffset +=
                                        trackLen * (ulong)(track.TrackRawBytesPerSector + 16);
                                    break;
                                case 4:
                                    if(firstTrack) currentOffset += 150 * (ulong)(track.TrackRawBytesPerSector + 96);
                                    track.TrackFileOffset       = currentOffset;
                                    track.TrackSubchannelFile   = track.TrackFile;
                                    track.TrackSubchannelOffset = currentOffset;
                                    track.TrackSubchannelType   = TrackSubchannelType.RawInterleaved;
                                    currentOffset +=
                                        trackLen * (ulong)(track.TrackRawBytesPerSector + 96);
                                    break;
                                default: throw new ImageNotSupportedException($"Unknown read mode {readMode}");
                            }

                            break;
                        // Mode 1 or DVD
                        case 1:
                            if(imageInfo.SectorSize < 2048) imageInfo.SectorSize = 2048;
                            track.TrackType           = TrackType.CdMode1;
                            track.TrackBytesPerSector = 2048;
                            switch(readMode)
                            {
                                case 0:
                                    track.TrackRawBytesPerSector = 2048;
                                    if(firstTrack) currentOffset += 150 * (ulong)track.TrackRawBytesPerSector;
                                    track.TrackFileOffset =  currentOffset;
                                    currentOffset         += trackLen * (ulong)track.TrackRawBytesPerSector;
                                    break;
                                case 1:
                                    throw
                                        new ImageNotSupportedException($"Invalid read mode {readMode} for this track");
                                case 2:
                                    track.TrackRawBytesPerSector =  2352;
                                    currentOffset                += trackLen * (ulong)track.TrackRawBytesPerSector;
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSync))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSync);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorHeader))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorHeader);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEcc))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEcc);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEccP))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEccP);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEccQ))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEccQ);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEdc))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEdc);
                                    break;
                                case 3:
                                    track.TrackRawBytesPerSector = 2352;
                                    if(firstTrack) currentOffset += 150 * (ulong)(track.TrackRawBytesPerSector + 16);
                                    track.TrackFileOffset       = currentOffset;
                                    track.TrackSubchannelFile   = track.TrackFile;
                                    track.TrackSubchannelOffset = currentOffset;
                                    track.TrackSubchannelType   = TrackSubchannelType.Q16Interleaved;
                                    currentOffset +=
                                        trackLen * (ulong)(track.TrackRawBytesPerSector + 16);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSync))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSync);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorHeader))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorHeader);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEcc))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEcc);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEccP))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEccP);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEccQ))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEccQ);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEdc))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEdc);
                                    break;
                                case 4:
                                    track.TrackRawBytesPerSector = 2352;
                                    if(firstTrack) currentOffset += 150 * (ulong)(track.TrackRawBytesPerSector + 96);
                                    track.TrackFileOffset       = currentOffset;
                                    track.TrackSubchannelFile   = track.TrackFile;
                                    track.TrackSubchannelOffset = currentOffset;
                                    track.TrackSubchannelType   = TrackSubchannelType.RawInterleaved;
                                    currentOffset +=
                                        trackLen * (ulong)(track.TrackRawBytesPerSector + 96);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSync))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSync);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorHeader))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorHeader);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEcc))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEcc);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEccP))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEccP);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEccQ))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEccQ);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorEdc))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorEdc);
                                    break;
                                default: throw new ImageNotSupportedException($"Unknown read mode {readMode}");
                            }

                            break;
                        // Mode 2
                        case 2:
                            if(imageInfo.SectorSize < 2336) imageInfo.SectorSize = 2336;
                            track.TrackType           = TrackType.CdMode2Formless;
                            track.TrackBytesPerSector = 2336;
                            switch(readMode)
                            {
                                case 0:
                                    throw
                                        new ImageNotSupportedException($"Invalid read mode {readMode} for this track");
                                case 1:
                                    track.TrackRawBytesPerSector = 2336;
                                    if(firstTrack) currentOffset += 150 * (ulong)track.TrackRawBytesPerSector;
                                    track.TrackFileOffset =  currentOffset;
                                    currentOffset         += trackLen * (ulong)track.TrackRawBytesPerSector;
                                    break;
                                case 2:
                                    track.TrackRawBytesPerSector =  2352;
                                    currentOffset                += trackLen * (ulong)track.TrackRawBytesPerSector;
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSync))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSync);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorHeader))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorHeader);
                                    break;
                                case 3:
                                    track.TrackRawBytesPerSector = 2352;
                                    if(firstTrack) currentOffset += 150 * (ulong)(track.TrackRawBytesPerSector + 16);
                                    track.TrackFileOffset       = currentOffset;
                                    track.TrackSubchannelFile   = track.TrackFile;
                                    track.TrackSubchannelOffset = currentOffset;
                                    track.TrackSubchannelType   = TrackSubchannelType.Q16Interleaved;
                                    currentOffset +=
                                        trackLen * (ulong)(track.TrackRawBytesPerSector + 16);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSync))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSync);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorHeader))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorHeader);
                                    break;
                                case 4:
                                    track.TrackRawBytesPerSector = 2352;
                                    if(firstTrack) currentOffset += 150 * (ulong)(track.TrackRawBytesPerSector + 96);
                                    track.TrackFileOffset       = currentOffset;
                                    track.TrackSubchannelFile   = track.TrackFile;
                                    track.TrackSubchannelOffset = currentOffset;
                                    track.TrackSubchannelType   = TrackSubchannelType.RawInterleaved;
                                    currentOffset +=
                                        trackLen * (ulong)(track.TrackRawBytesPerSector + 96);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSync))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSync);
                                    if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorHeader))
                                        imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorHeader);
                                    break;
                                default: throw new ImageNotSupportedException($"Unknown read mode {readMode}");
                            }

                            break;
                        default: throw new ImageNotSupportedException($"Unknown track mode {trackMode}");
                    }

                    track.TrackFile   = imageFilter.GetFilename();
                    track.TrackFilter = imageFilter;
                    if(track.TrackSubchannelType != TrackSubchannelType.None)
                    {
                        track.TrackSubchannelFile   = imageFilter.GetFilename();
                        track.TrackSubchannelFilter = imageFilter;
                        if(!imageInfo.ReadableSectorTags.Contains(SectorTagType.CdSectorSubchannel))
                            imageInfo.ReadableSectorTags.Add(SectorTagType.CdSectorSubchannel);
                    }

                    Partition partition = new Partition
                    {
                        Description = track.TrackDescription,
                        Size        = (ulong)(trackLen * track.TrackBytesPerSector),
                        Length      = trackLen,
                        Sequence    = track.TrackSequence,
                        Offset      = track.TrackFileOffset,
                        Start       = track.TrackStartSector,
                        Type        = track.TrackType.ToString()
                    };
                    imageInfo.Sectors += partition.Length;
                    Partitions.Add(partition);
                    offsetmap.Add(track.TrackSequence, track.TrackStartSector);
                    Tracks.Add(track);
                    trackFlags.Add(track.TrackSequence, (byte)(trackCtl & 0xFF));
                    addedATrack = true;
                }

                if(!addedATrack) continue;

                lastSessionTrack = session.EndTrack;
                Sessions.Add(session);
                DicConsole.DebugWriteLine("DiscJuggler plugin", "session.StartTrack = {0}",  session.StartTrack);
                DicConsole.DebugWriteLine("DiscJuggler plugin", "session.StartSector = {0}", session.StartSector);
                DicConsole.DebugWriteLine("DiscJuggler plugin", "session.EndTrack = {0}",    session.EndTrack);
                DicConsole.DebugWriteLine("DiscJuggler plugin", "session.EndSector = {0}",   session.EndSector);
                DicConsole.DebugWriteLine("DiscJuggler plugin", "session.SessionSequence = {0}",
                                          session.SessionSequence);
            }

            // Skip unknown
            position += 16;

            DicConsole.DebugWriteLine("DiscJuggler plugin", "Current position = {0}", position);
            byte[] filenameB = new byte[descriptor[position]];
            position++;
            Array.Copy(descriptor, position, filenameB, 0, filenameB.Length);
            position += filenameB.Length;
            string filename = Path.GetFileName(Encoding.Default.GetString(filenameB));
            DicConsole.DebugWriteLine("DiscJuggler plugin", "filename = {0}", filename);

            // Skip unknown
            position += 29;

            mediumType =  BitConverter.ToUInt16(descriptor, position);
            position   += 2;
            DicConsole.DebugWriteLine("DiscJuggler plugin", "mediumType = {0}", mediumType);

            uint discSize = BitConverter.ToUInt32(descriptor, position);
            position += 4;
            DicConsole.DebugWriteLine("DiscJuggler plugin", "discSize = {0}", discSize);

            byte[] volidB = new byte[descriptor[position]];
            position++;
            Array.Copy(descriptor, position, volidB, 0, volidB.Length);
            position += volidB.Length;
            string volid = Path.GetFileName(Encoding.Default.GetString(volidB));
            DicConsole.DebugWriteLine("DiscJuggler plugin", "volid = {0}", volid);

            // Skip unknown
            position += 9;

            byte[] mcn = new byte[13];
            Array.Copy(descriptor, position, mcn, 0, 13);
            DicConsole.DebugWriteLine("DiscJuggler plugin", "mcn = {0}", StringHandlers.CToString(mcn));
            position += 13;
            uint mcnValid = BitConverter.ToUInt32(descriptor, position);
            DicConsole.DebugWriteLine("DiscJuggler plugin", "mcn_valid = {0}", mcnValid);
            position += 4;

            uint cdtextLen = BitConverter.ToUInt32(descriptor, position);
            DicConsole.DebugWriteLine("DiscJuggler plugin", "cdtextLen = {0}", cdtextLen);
            position += 4;
            if(cdtextLen > 0)
            {
                cdtext = new byte[cdtextLen];
                Array.Copy(descriptor, position, cdtext, 0, cdtextLen);
                position += (int)cdtextLen;
                imageInfo.ReadableMediaTags.Add(MediaTagType.CD_TEXT);
            }

            // Skip unknown
            position += 12;

            DicConsole.DebugWriteLine("DiscJuggler plugin", "End position = {0}", position);

            if(imageInfo.MediaType == MediaType.CDROM)
            {
                bool data       = false;
                bool mode2      = false;
                bool firstaudio = false;
                bool firstdata  = false;
                bool audio      = false;

                for(int i = 0; i < Tracks.Count; i++)
                {
                    // First track is audio
                    firstaudio |= i == 0 && Tracks[i].TrackType == TrackType.Audio;

                    // First track is data
                    firstdata |= i == 0 && Tracks[i].TrackType != TrackType.Audio;

                    // Any non first track is data
                    data |= i != 0 && Tracks[i].TrackType != TrackType.Audio;

                    // Any non first track is audio
                    audio |= i != 0 && Tracks[i].TrackType == TrackType.Audio;

                    switch(Tracks[i].TrackType)
                    {
                        case TrackType.CdMode2Form1:
                        case TrackType.CdMode2Form2:
                        case TrackType.CdMode2Formless:
                            mode2 = true;
                            break;
                    }
                }

                if(!data                                         && !firstdata) imageInfo.MediaType = MediaType.CDDA;
                else if(firstaudio && data && Sessions.Count > 1 && mode2) imageInfo.MediaType      = MediaType.CDPLUS;
                else if(firstdata && audio || mode2) imageInfo.MediaType                            = MediaType.CDROMXA;
                else if(!audio) imageInfo.MediaType                                                 = MediaType.CDROM;
                else imageInfo.MediaType                                                            = MediaType.CD;
            }

            imageInfo.Application          = "DiscJuggler";
            imageInfo.ImageSize            = (ulong)imageFilter.GetDataForkLength();
            imageInfo.CreationTime         = imageFilter.GetCreationTime();
            imageInfo.LastModificationTime = imageFilter.GetLastWriteTime();
            imageInfo.XmlMediaType         = XmlMediaType.OpticalDisc;

            return true;
        }

        public byte[] ReadDiskTag(MediaTagType tag)
        {
            switch(tag)
            {
                case MediaTagType.CD_TEXT:
                {
                    if(cdtext != null && cdtext.Length > 0) return cdtext;

                    throw new FeatureNotPresentImageException("Image does not contain CD-TEXT information.");
                }
                default:
                    throw new FeatureSupportedButNotImplementedImageException("Feature not supported by image format");
            }
        }

        public byte[] ReadSector(ulong sectorAddress) => ReadSectors(sectorAddress, 1);

        public byte[] ReadSectorTag(ulong sectorAddress, SectorTagType tag) => ReadSectorsTag(sectorAddress, 1, tag);

        public byte[] ReadSector(ulong sectorAddress, uint track) => ReadSectors(sectorAddress, 1, track);

        public byte[] ReadSectorTag(ulong sectorAddress, uint track, SectorTagType tag) =>
            ReadSectorsTag(sectorAddress, 1, track, tag);

        public byte[] ReadSectors(ulong sectorAddress, uint length)
        {
            foreach(KeyValuePair<uint, ulong> kvp in from kvp in offsetmap
                                                     where sectorAddress >= kvp.Value
                                                     from track in Tracks
                                                     where track.TrackSequence == kvp.Key
                                                     where sectorAddress       < track.TrackEndSector
                                                     select kvp)
                return ReadSectors(sectorAddress - kvp.Value, length, kvp.Key);

            throw new ArgumentOutOfRangeException(nameof(sectorAddress), $"Sector address {sectorAddress} not found");
        }

        public byte[] ReadSectorsTag(ulong sectorAddress, uint length, SectorTagType tag)
        {
            foreach(KeyValuePair<uint, ulong> kvp in offsetmap
                                                    .Where(kvp => sectorAddress >= kvp.Value)
                                                    .Where(kvp => Tracks
                                                                 .Where(track => track.TrackSequence == kvp.Key)
                                                                 .Any(track => sectorAddress <
                                                                               track.TrackEndSector)))
                return ReadSectorsTag(sectorAddress - kvp.Value, length, kvp.Key, tag);

            throw new ArgumentOutOfRangeException(nameof(sectorAddress), $"Sector address {sectorAddress} not found");
        }

        public byte[] ReadSectors(ulong sectorAddress, uint length, uint track)
        {
            Track dicTrack = new Track {TrackSequence = 0};

            foreach(Track linqTrack in Tracks.Where(linqTrack => linqTrack.TrackSequence == track))
            {
                dicTrack = linqTrack;
                break;
            }

            if(dicTrack.TrackSequence == 0)
                throw new ArgumentOutOfRangeException(nameof(track), "Track does not exist in disc image");

            if(length + sectorAddress > dicTrack.TrackEndSector)
                throw new ArgumentOutOfRangeException(nameof(length),
                                                      $"Requested more sectors ({length + sectorAddress}) than present in track ({dicTrack.TrackEndSector}), won't cross tracks");

            uint sectorOffset;
            uint sectorSize;
            uint sectorSkip;

            switch(dicTrack.TrackType)
            {
                case TrackType.Audio:
                {
                    sectorOffset = 0;
                    sectorSize   = 2352;
                    sectorSkip   = 0;
                    break;
                }
                case TrackType.CdMode1:
                    if(dicTrack.TrackRawBytesPerSector == 2352)
                    {
                        sectorOffset = 16;
                        sectorSize   = 2048;
                        sectorSkip   = 288;
                    }
                    else
                    {
                        sectorOffset = 0;
                        sectorSize   = 2048;
                        sectorSkip   = 0;
                    }

                    break;
                case TrackType.CdMode2Formless:
                    if(dicTrack.TrackRawBytesPerSector == 2352)
                    {
                        sectorOffset = 16;
                        sectorSize   = 2336;
                        sectorSkip   = 0;
                    }
                    else
                    {
                        sectorOffset = 0;
                        sectorSize   = 2336;
                        sectorSkip   = 0;
                    }

                    break;
                default: throw new FeatureSupportedButNotImplementedImageException("Unsupported track type");
            }

            switch(dicTrack.TrackSubchannelType)
            {
                case TrackSubchannelType.None:
                    sectorSkip += 0;
                    break;
                case TrackSubchannelType.Q16Interleaved:
                    sectorSkip += 16;
                    break;
                case TrackSubchannelType.PackedInterleaved:
                    sectorSkip += 96;
                    break;
                default: throw new FeatureSupportedButNotImplementedImageException("Unsupported subchannel type");
            }

            byte[] buffer = new byte[sectorSize * length];

            imageStream.Seek((long)(dicTrack.TrackFileOffset + sectorAddress * (ulong)dicTrack.TrackRawBytesPerSector),
                             SeekOrigin.Begin);
            if(sectorOffset == 0 && sectorSkip == 0) imageStream.Read(buffer, 0, buffer.Length);
            else
                for(int i = 0; i < length; i++)
                {
                    byte[] sector = new byte[sectorSize];
                    imageStream.Seek(sectorOffset, SeekOrigin.Current);
                    imageStream.Read(sector, 0, sector.Length);
                    imageStream.Seek(sectorSkip, SeekOrigin.Current);
                    Array.Copy(sector, 0, buffer, i * sectorSize, sectorSize);
                }

            return buffer;
        }

        public byte[] ReadSectorsTag(ulong sectorAddress, uint length, uint track, SectorTagType tag)
        {
            Track dicTrack = new Track {TrackSequence = 0};

            foreach(Track linqTrack in Tracks.Where(linqTrack => linqTrack.TrackSequence == track))
            {
                dicTrack = linqTrack;
                break;
            }

            if(dicTrack.TrackSequence == 0)
                throw new ArgumentOutOfRangeException(nameof(track), "Track does not exist in disc image");

            if(length + sectorAddress > dicTrack.TrackEndSector)
                throw new ArgumentOutOfRangeException(nameof(length),
                                                      $"Requested more sectors ({length + sectorAddress}) than present in track ({dicTrack.TrackEndSector}), won't cross tracks");

            if(dicTrack.TrackType == TrackType.Data)
                throw new ArgumentException("Unsupported tag requested", nameof(tag));

            switch(tag)
            {
                case SectorTagType.CdSectorEcc:
                case SectorTagType.CdSectorEccP:
                case SectorTagType.CdSectorEccQ:
                case SectorTagType.CdSectorEdc:
                case SectorTagType.CdSectorHeader:
                case SectorTagType.CdSectorSubchannel:
                case SectorTagType.CdSectorSubHeader:
                case SectorTagType.CdSectorSync: break;
                case SectorTagType.CdTrackFlags:
                    if(trackFlags.TryGetValue(track, out byte flag)) return new[] {flag};

                    throw new ArgumentException("Unsupported tag requested", nameof(tag));
                default: throw new ArgumentException("Unsupported tag requested", nameof(tag));
            }

            uint sectorOffset;
            uint sectorSize;
            uint sectorSkip;

            switch(dicTrack.TrackType)
            {
                case TrackType.CdMode1:
                    if(dicTrack.TrackRawBytesPerSector != 2352)
                        throw new ArgumentException("Unsupported tag requested for this track", nameof(tag));

                    switch(tag)
                    {
                        case SectorTagType.CdSectorSync:
                        {
                            sectorOffset = 0;
                            sectorSize   = 12;
                            sectorSkip   = 2340;
                            break;
                        }
                        case SectorTagType.CdSectorHeader:
                        {
                            sectorOffset = 12;
                            sectorSize   = 4;
                            sectorSkip   = 2336;
                            break;
                        }
                        case SectorTagType.CdSectorSubHeader:
                            throw new ArgumentException("Unsupported tag requested for this track", nameof(tag));
                        case SectorTagType.CdSectorEcc:
                        {
                            sectorOffset = 2076;
                            sectorSize   = 276;
                            sectorSkip   = 0;
                            break;
                        }
                        case SectorTagType.CdSectorEccP:
                        {
                            sectorOffset = 2076;
                            sectorSize   = 172;
                            sectorSkip   = 104;
                            break;
                        }
                        case SectorTagType.CdSectorEccQ:
                        {
                            sectorOffset = 2248;
                            sectorSize   = 104;
                            sectorSkip   = 0;
                            break;
                        }
                        case SectorTagType.CdSectorEdc:
                        {
                            sectorOffset = 2064;
                            sectorSize   = 4;
                            sectorSkip   = 284;
                            break;
                        }
                        case SectorTagType.CdSectorSubchannel:
                            switch(dicTrack.TrackSubchannelType)
                            {
                                case TrackSubchannelType.None:
                                    throw new ArgumentException("Unsupported tag requested for this track",
                                                                nameof(tag));
                                case TrackSubchannelType.Q16Interleaved:
                                    throw new ArgumentException("Q16 subchannel not yet supported");
                            }

                            sectorOffset = 2352;
                            sectorSize   = 96;
                            sectorSkip   = 0;
                            break;
                        default: throw new ArgumentException("Unsupported tag requested", nameof(tag));
                    }

                    break;
                case TrackType.CdMode2Formless:
                    if(dicTrack.TrackRawBytesPerSector != 2352)
                        throw new ArgumentException("Unsupported tag requested for this track", nameof(tag));

                {
                    switch(tag)
                    {
                        case SectorTagType.CdSectorSync:
                        case SectorTagType.CdSectorHeader:
                        case SectorTagType.CdSectorEcc:
                        case SectorTagType.CdSectorEccP:
                        case SectorTagType.CdSectorEccQ:
                            throw new ArgumentException("Unsupported tag requested for this track", nameof(tag));
                        case SectorTagType.CdSectorSubHeader:
                        {
                            sectorOffset = 0;
                            sectorSize   = 8;
                            sectorSkip   = 2328;
                            break;
                        }
                        case SectorTagType.CdSectorEdc:
                        {
                            sectorOffset = 2332;
                            sectorSize   = 4;
                            sectorSkip   = 0;
                            break;
                        }
                        case SectorTagType.CdSectorSubchannel:
                            switch(dicTrack.TrackSubchannelType)
                            {
                                case TrackSubchannelType.None:
                                    throw new ArgumentException("Unsupported tag requested for this track",
                                                                nameof(tag));
                                case TrackSubchannelType.Q16Interleaved:
                                    throw new ArgumentException("Q16 subchannel not yet supported");
                            }

                            sectorOffset = 2352;
                            sectorSize   = 96;
                            sectorSkip   = 0;
                            break;
                        default: throw new ArgumentException("Unsupported tag requested", nameof(tag));
                    }

                    break;
                }
                case TrackType.Audio:
                {
                    switch(tag)
                    {
                        case SectorTagType.CdSectorSubchannel:
                            switch(dicTrack.TrackSubchannelType)
                            {
                                case TrackSubchannelType.None:
                                    throw new ArgumentException("Unsupported tag requested for this track",
                                                                nameof(tag));
                                case TrackSubchannelType.Q16Interleaved:
                                    throw new ArgumentException("Q16 subchannel not yet supported");
                            }

                            sectorOffset = 2352;
                            sectorSize   = 96;
                            sectorSkip   = 0;
                            break;
                        default: throw new ArgumentException("Unsupported tag requested", nameof(tag));
                    }

                    break;
                }
                default: throw new FeatureSupportedButNotImplementedImageException("Unsupported track type");
            }

            switch(dicTrack.TrackSubchannelType)
            {
                case TrackSubchannelType.None:
                    sectorSkip += 0;
                    break;
                case TrackSubchannelType.Q16Interleaved:
                    sectorSkip += 16;
                    break;
                case TrackSubchannelType.PackedInterleaved:
                    sectorSkip += 96;
                    break;
                default: throw new FeatureSupportedButNotImplementedImageException("Unsupported subchannel type");
            }

            byte[] buffer = new byte[sectorSize * length];

            imageStream.Seek((long)(dicTrack.TrackFileOffset + sectorAddress * (ulong)dicTrack.TrackRawBytesPerSector),
                             SeekOrigin.Begin);
            if(sectorOffset == 0 && sectorSkip == 0) imageStream.Read(buffer, 0, buffer.Length);
            else
                for(int i = 0; i < length; i++)
                {
                    byte[] sector = new byte[sectorSize];
                    imageStream.Seek(sectorOffset, SeekOrigin.Current);
                    imageStream.Read(sector, 0, sector.Length);
                    imageStream.Seek(sectorSkip, SeekOrigin.Current);
                    Array.Copy(sector, 0, buffer, i * sectorSize, sectorSize);
                }

            return buffer;
        }

        public byte[] ReadSectorLong(ulong sectorAddress) => ReadSectorsLong(sectorAddress, 1);

        public byte[] ReadSectorLong(ulong sectorAddress, uint track) => ReadSectorsLong(sectorAddress, 1, track);

        public byte[] ReadSectorsLong(ulong sectorAddress, uint length)
        {
            foreach(KeyValuePair<uint, ulong> kvp in from kvp in offsetmap
                                                     where sectorAddress >= kvp.Value
                                                     from track in Tracks
                                                     where track.TrackSequence == kvp.Key
                                                     where sectorAddress        - kvp.Value <
                                                           track.TrackEndSector - track.TrackStartSector
                                                     select kvp)
                return ReadSectorsLong(sectorAddress - kvp.Value, length, kvp.Key);

            throw new ArgumentOutOfRangeException(nameof(sectorAddress), $"Sector address {sectorAddress} not found");
        }

        public byte[] ReadSectorsLong(ulong sectorAddress, uint length, uint track)
        {
            Track dicTrack = new Track {TrackSequence = 0};

            foreach(Track linqTrack in Tracks.Where(linqTrack => linqTrack.TrackSequence == track))
            {
                dicTrack = linqTrack;
                break;
            }

            if(dicTrack.TrackSequence == 0)
                throw new ArgumentOutOfRangeException(nameof(track), "Track does not exist in disc image");

            if(length + sectorAddress > dicTrack.TrackEndSector)
                throw new ArgumentOutOfRangeException(nameof(length),
                                                      $"Requested more sectors ({length + sectorAddress}) than present in track ({dicTrack.TrackEndSector}), won't cross tracks");

            uint sectorSize = (uint)dicTrack.TrackRawBytesPerSector;
            uint sectorSkip = 0;

            switch(dicTrack.TrackSubchannelType)
            {
                case TrackSubchannelType.None:
                    sectorSkip += 0;
                    break;
                case TrackSubchannelType.Q16Interleaved:
                    sectorSkip += 16;
                    break;
                case TrackSubchannelType.PackedInterleaved:
                    sectorSkip += 96;
                    break;
                default: throw new FeatureSupportedButNotImplementedImageException("Unsupported subchannel type");
            }

            byte[] buffer = new byte[sectorSize * length];

            imageStream.Seek((long)(dicTrack.TrackFileOffset + sectorAddress * (ulong)dicTrack.TrackRawBytesPerSector),
                             SeekOrigin.Begin);
            if(sectorSkip == 0) imageStream.Read(buffer, 0, buffer.Length);
            else
                for(int i = 0; i < length; i++)
                {
                    byte[] sector = new byte[sectorSize];
                    imageStream.Read(sector, 0, sector.Length);
                    imageStream.Seek(sectorSkip, SeekOrigin.Current);
                    Array.Copy(sector, 0, buffer, i * sectorSize, sectorSize);
                }

            return buffer;
        }

        public List<Track> GetSessionTracks(Session session)
        {
            if(Sessions.Contains(session)) return GetSessionTracks(session.SessionSequence);

            throw new ImageNotSupportedException("Session does not exist in disc image");
        }

        public List<Track> GetSessionTracks(ushort session)
        {
            return Tracks.Where(track => track.TrackSession == session).ToList();
        }
    }
}