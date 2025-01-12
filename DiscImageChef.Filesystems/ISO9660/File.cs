// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : File.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : ISO9660 filesystem plugin.
//
// --[ Description ] ----------------------------------------------------------
//
//     Handles file and extents.
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
// In the loving memory of Facunda "Tata" Suárez Domínguez, R.I.P. 2019/07/24
// ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DiscImageChef.CommonTypes.Structs;
using DiscImageChef.Helpers;
using FileAttributes = DiscImageChef.CommonTypes.Structs.FileAttributes;

namespace DiscImageChef.Filesystems.ISO9660
{
    public partial class ISO9660
    {
        public Errno MapBlock(string path, long fileBlock, out long deviceBlock)
        {
            deviceBlock = 0;
            if(!mounted) return Errno.AccessDenied;

            Errno err = GetFileEntry(path, out DecodedDirectoryEntry entry);
            if(err != Errno.NoError) return err;

            if(entry.Flags.HasFlag(FileFlags.Directory) && !debug) return Errno.IsDirectory;

            // TODO: Multi-extents
            if(entry.Extents.Count > 1) return Errno.NotImplemented;

            deviceBlock = entry.Extents[0].extent + fileBlock;

            return Errno.NoError;
        }

        public Errno GetAttributes(string path, out FileAttributes attributes)
        {
            attributes = new FileAttributes();
            if(!mounted) return Errno.AccessDenied;

            Errno err = Stat(path, out FileEntryInfo stat);

            if(err != Errno.NoError) return err;

            attributes = stat.Attributes;

            return Errno.NoError;
        }

        // TODO: Resolve symbolic link
        public Errno Read(string path, long offset, long size, ref byte[] buf)
        {
            buf = null;
            if(!mounted) return Errno.AccessDenied;

            Errno err = GetFileEntry(path, out DecodedDirectoryEntry entry);
            if(err != Errno.NoError) return err;

            if(entry.Flags.HasFlag(FileFlags.Directory) && !debug) return Errno.IsDirectory;

            if(entry.Extents is null) return Errno.InvalidArgument;

            if(entry.Size == 0)
            {
                buf = new byte[0];
                return Errno.NoError;
            }

            if(offset >= (long)entry.Size) return Errno.InvalidArgument;

            if(size + offset >= (long)entry.Size) size = (long)entry.Size - offset;

            offset += entry.XattrLength;

            long firstSector    = offset                  / 2048;
            long offsetInSector = offset                  % 2048;
            long sizeInSectors  = (size + offsetInSector) / 2048;
            if((size + offsetInSector) % 2048 > 0) sizeInSectors++;

            if(entry.Extents.Count == 1)
            {
                // No need to check mode, if we know it is CD-DA
                byte[] buffer =
                    entry.CdiSystemArea != null &&
                    ((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.DigitalAudio)
                        ? image.ReadSectors((ulong)(entry.Extents[0].extent + firstSector), (uint)sizeInSectors)
                        : ReadSectors((ulong)(entry.Extents[0].extent       + firstSector), (uint)sizeInSectors);

                buf = new byte[size];
                Array.Copy(buffer, offsetInSector, buf, 0, size);
            }
            else buf = ReadWithExtents(offset, size, entry.Extents);

            return Errno.NoError;
        }

        public Errno Stat(string path, out FileEntryInfo stat)
        {
            stat = null;
            if(!mounted) return Errno.AccessDenied;

            Errno err = GetFileEntry(path, out DecodedDirectoryEntry entry);
            if(err != Errno.NoError) return err;

            stat = new FileEntryInfo
            {
                Attributes       = new FileAttributes(),
                Blocks           = (long)(entry.Size / 2048), // TODO: XA
                BlockSize        = 2048,
                Length           = (long)entry.Size,
                Inode            = entry.Extents?[0].extent ?? 0,
                Links            = 1,
                LastWriteTimeUtc = entry.Timestamp
            };

            if(entry.Size % 2048 > 0) stat.Blocks++;

            if(entry.Flags.HasFlag(FileFlags.Directory)) stat.Attributes |= FileAttributes.Directory;
            if(entry.Flags.HasFlag(FileFlags.Hidden)) stat.Attributes    |= FileAttributes.Hidden;

            if(entry.FinderInfo != null)
            {
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kIsAlias)) stat.Attributes     |= FileAttributes.Alias;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kIsInvisible)) stat.Attributes |= FileAttributes.Hidden;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kHasBeenInited))
                    stat.Attributes |= FileAttributes.HasBeenInited;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kHasCustomIcon))
                    stat.Attributes |= FileAttributes.HasCustomIcon;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kHasNoINITs))
                    stat.Attributes |= FileAttributes.HasNoINITs;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kIsOnDesk)) stat.Attributes |= FileAttributes.IsOnDesk;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kIsShared)) stat.Attributes |= FileAttributes.Shared;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kIsStationery))
                    stat.Attributes |= FileAttributes.Stationery;
                if(entry.FinderInfo.fdFlags.HasFlag(FinderFlags.kHasBundle)) stat.Attributes |= FileAttributes.Bundle;
            }

            if(entry.AppleIcon != null) stat.Attributes |= FileAttributes.HasCustomIcon;

            if(entry.XA != null)
            {
                if(entry.XA.Value.attributes.HasFlag(XaAttributes.GroupExecute)) stat.Mode  |= 8;
                if(entry.XA.Value.attributes.HasFlag(XaAttributes.GroupRead)) stat.Mode     |= 32;
                if(entry.XA.Value.attributes.HasFlag(XaAttributes.OwnerExecute)) stat.Mode  |= 64;
                if(entry.XA.Value.attributes.HasFlag(XaAttributes.OwnerRead)) stat.Mode     |= 256;
                if(entry.XA.Value.attributes.HasFlag(XaAttributes.SystemExecute)) stat.Mode |= 1;
                if(entry.XA.Value.attributes.HasFlag(XaAttributes.SystemRead)) stat.Mode    |= 4;

                stat.UID   = entry.XA.Value.user;
                stat.GID   = entry.XA.Value.group;
                stat.Inode = entry.XA.Value.filenumber;
            }

            if(entry.PosixAttributes != null)
            {
                stat.Mode = (uint?)entry.PosixAttributes.Value.st_mode & 0x0FFF;
                if(entry.PosixAttributes.Value.st_mode.HasFlag(PosixMode.Block))
                    stat.Attributes |= FileAttributes.BlockDevice;
                if(entry.PosixAttributes.Value.st_mode.HasFlag(PosixMode.Character))
                    stat.Attributes |= FileAttributes.CharDevice;
                if(entry.PosixAttributes.Value.st_mode.HasFlag(PosixMode.Pipe)) stat.Attributes |= FileAttributes.Pipe;
                if(entry.PosixAttributes.Value.st_mode.HasFlag(PosixMode.Socket))
                    stat.Attributes |= FileAttributes.Socket;
                if(entry.PosixAttributes.Value.st_mode.HasFlag(PosixMode.Symlink))
                    stat.Attributes |= FileAttributes.Symlink;
                stat.Links = entry.PosixAttributes.Value.st_nlink;
                stat.UID   = entry.PosixAttributes.Value.st_uid;
                stat.GID   = entry.PosixAttributes.Value.st_gid;
                stat.Inode = entry.PosixAttributes.Value.st_ino;
            }
            else if(entry.PosixAttributesOld != null)
            {
                stat.Mode = (uint?)entry.PosixAttributesOld.Value.st_mode & 0x0FFF;
                if(entry.PosixAttributesOld.Value.st_mode.HasFlag(PosixMode.Block))
                    stat.Attributes |= FileAttributes.BlockDevice;
                if(entry.PosixAttributesOld.Value.st_mode.HasFlag(PosixMode.Character))
                    stat.Attributes |= FileAttributes.CharDevice;
                if(entry.PosixAttributesOld.Value.st_mode.HasFlag(PosixMode.Pipe))
                    stat.Attributes |= FileAttributes.Pipe;
                if(entry.PosixAttributesOld.Value.st_mode.HasFlag(PosixMode.Socket))
                    stat.Attributes |= FileAttributes.Socket;
                if(entry.PosixAttributesOld.Value.st_mode.HasFlag(PosixMode.Symlink))
                    stat.Attributes |= FileAttributes.Symlink;
                stat.Links = entry.PosixAttributesOld.Value.st_nlink;
                stat.UID   = entry.PosixAttributesOld.Value.st_uid;
                stat.GID   = entry.PosixAttributesOld.Value.st_gid;
            }

            if(entry.AmigaProtection != null)
            {
                if(entry.AmigaProtection.Value.Multiuser.HasFlag(AmigaMultiuser.GroupExec)) stat.Mode    |= 8;
                if(entry.AmigaProtection.Value.Multiuser.HasFlag(AmigaMultiuser.GroupRead)) stat.Mode    |= 32;
                if(entry.AmigaProtection.Value.Multiuser.HasFlag(AmigaMultiuser.GroupWrite)) stat.Mode   |= 16;
                if(entry.AmigaProtection.Value.Multiuser.HasFlag(AmigaMultiuser.OtherExec)) stat.Mode    |= 1;
                if(entry.AmigaProtection.Value.Multiuser.HasFlag(AmigaMultiuser.OtherRead)) stat.Mode    |= 4;
                if(entry.AmigaProtection.Value.Multiuser.HasFlag(AmigaMultiuser.OtherWrite)) stat.Mode   |= 2;
                if(entry.AmigaProtection.Value.Protection.HasFlag(AmigaAttributes.OwnerExec)) stat.Mode  |= 64;
                if(entry.AmigaProtection.Value.Protection.HasFlag(AmigaAttributes.OwnerRead)) stat.Mode  |= 256;
                if(entry.AmigaProtection.Value.Protection.HasFlag(AmigaAttributes.OwnerWrite)) stat.Mode |= 128;
                if(entry.AmigaProtection.Value.Protection.HasFlag(AmigaAttributes.Archive))
                    stat.Attributes |= FileAttributes.Archive;
            }

            if(entry.PosixDeviceNumber != null)
                stat.DeviceNo = (entry.PosixDeviceNumber.Value.dev_t_high << 32) +
                                entry.PosixDeviceNumber.Value.dev_t_low;

            if(entry.RripModify != null) stat.LastWriteTimeUtc = DecodeIsoDateTime(entry.RripModify);

            if(entry.RripAccess != null) stat.AccessTimeUtc = DecodeIsoDateTime(entry.RripAccess);

            if(entry.RripAttributeChange != null)
                stat.StatusChangeTimeUtc = DecodeIsoDateTime(entry.RripAttributeChange);

            if(entry.RripBackup != null) stat.BackupTimeUtc = DecodeIsoDateTime(entry.RripBackup);

            if(entry.SymbolicLink != null) stat.Attributes |= FileAttributes.Symlink;

            if(entry.XattrLength == 0 || cdi || highSierra) return Errno.NoError;

            if(entry.CdiSystemArea != null)
            {
                stat.UID = entry.CdiSystemArea.Value.owner;
                stat.GID = entry.CdiSystemArea.Value.group;
                if(((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.GroupExecute))
                    stat.Mode |= 8;
                if(((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.GroupRead))
                    stat.Mode |= 32;
                if(((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.OtherExecute))
                    stat.Mode |= 1;
                if(((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.OtherRead))
                    stat.Mode |= 4;
                if(((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.OwnerExecute))
                    stat.Mode |= 64;
                if(((CdiAttributes)entry.CdiSystemArea.Value.attributes).HasFlag(CdiAttributes.OwnerRead))
                    stat.Mode |= 256;
            }

            uint eaSizeInSectors = (uint)(entry.XattrLength / 2048);
            if(entry.XattrLength % 2048 > 0) eaSizeInSectors++;

            byte[] ea = ReadSectors(entry.Extents[0].extent, eaSizeInSectors);

            ExtendedAttributeRecord ear = Marshal.ByteArrayToStructureLittleEndian<ExtendedAttributeRecord>(ea);

            stat.UID = ear.owner;
            stat.GID = ear.group;

            stat.Mode = 0;
            if(ear.permissions.HasFlag(Permissions.GroupExecute)) stat.Mode |= 8;
            if(ear.permissions.HasFlag(Permissions.GroupRead)) stat.Mode    |= 32;
            if(ear.permissions.HasFlag(Permissions.OwnerExecute)) stat.Mode |= 64;
            if(ear.permissions.HasFlag(Permissions.OwnerRead)) stat.Mode    |= 256;
            if(ear.permissions.HasFlag(Permissions.OtherExecute)) stat.Mode |= 1;
            if(ear.permissions.HasFlag(Permissions.OtherRead)) stat.Mode    |= 4;

            stat.CreationTimeUtc  = DateHandlers.Iso9660ToDateTime(ear.creation_date);
            stat.LastWriteTimeUtc = DateHandlers.Iso9660ToDateTime(ear.modification_date);

            return Errno.NoError;
        }

        public Errno ReadLink(string path, out string dest)
        {
            dest = null;

            Errno err = GetFileEntry(path, out DecodedDirectoryEntry entry);
            if(err != Errno.NoError) return err;

            if(entry.SymbolicLink is null) return Errno.InvalidArgument;

            dest = entry.SymbolicLink;

            return Errno.NoError;
        }

        Errno GetFileEntry(string path, out DecodedDirectoryEntry entry)
        {
            entry = null;

            string cutPath = path.StartsWith("/", StringComparison.Ordinal)
                                 ? path.Substring(1).ToLower(CultureInfo.CurrentUICulture)
                                 : path.ToLower(CultureInfo.CurrentUICulture);
            string[] pieces = cutPath.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

            if(pieces.Length == 0) return Errno.InvalidArgument;

            string parentPath = string.Join("/", pieces, 0, pieces.Length - 1);

            if(!directoryCache.TryGetValue(parentPath, out _))
            {
                Errno err = ReadDir(parentPath, out _);

                if(err != Errno.NoError) return err;
            }

            Dictionary<string, DecodedDirectoryEntry> parent;

            if(pieces.Length == 1) parent = rootDirectoryCache;
            else if(!directoryCache.TryGetValue(parentPath, out parent)) return Errno.InvalidArgument;

            KeyValuePair<string, DecodedDirectoryEntry> dirent =
                parent.FirstOrDefault(t => t.Key.ToLower(CultureInfo.CurrentUICulture) == pieces[pieces.Length - 1]);

            if(string.IsNullOrEmpty(dirent.Key))
            {
                if(!joliet && !pieces[pieces.Length - 1].EndsWith(";1", StringComparison.Ordinal))
                {
                    dirent = parent.FirstOrDefault(t => t.Key.ToLower(CultureInfo.CurrentUICulture) ==
                                                        pieces[pieces.Length - 1] + ";1");

                    if(string.IsNullOrEmpty(dirent.Key)) return Errno.NoSuchFile;
                }
                else return Errno.NoSuchFile;
            }

            entry = dirent.Value;
            return Errno.NoError;
        }

        // Cannot think how to make this faster, as we don't know the mode sector until it is read, but we have size in bytes
        byte[] ReadWithExtents(long offset, long size, List<(uint extent, uint size)> extents)
        {
            MemoryStream ms             = new MemoryStream();
            long         currentFilePos = 0;

            for(int i = 0; i < extents.Count; i++)
            {
                if(offset - currentFilePos >= extents[i].size)
                {
                    currentFilePos += extents[i].size;
                    continue;
                }

                long leftExtentSize      = extents[i].size;
                uint currentExtentSector = 0;

                while(leftExtentSize > 0)
                {
                    byte[] sector = ReadSectors(extents[i].extent + currentExtentSector, 1);

                    if(offset - currentFilePos > sector.Length)
                    {
                        currentExtentSector++;
                        leftExtentSize -= sector.Length;
                        currentFilePos += sector.Length;
                        continue;
                    }

                    if(offset - currentFilePos > 0)
                        ms.Write(sector, (int)(offset - currentFilePos),
                                 (int)(sector.Length  - (offset - currentFilePos)));
                    else ms.Write(sector, 0, sector.Length);

                    currentExtentSector++;
                    leftExtentSize -= sector.Length;
                    currentFilePos += sector.Length;

                    if(ms.Length >= size) break;
                }

                if(ms.Length >= size) break;
            }

            if(ms.Length >= size) ms.SetLength(size);

            return ms.ToArray();
        }
    }
}