// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : File.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Apple Lisa filesystem plugin.
//
// --[ Description ] ----------------------------------------------------------
//
//     Methods to handle files.
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
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Structs;
using DiscImageChef.Console;
using DiscImageChef.Decoders;

namespace DiscImageChef.Filesystems.LisaFS
{
    public partial class LisaFS
    {
        public Errno GetAttributes(string path, out FileAttributes attributes)
        {
            attributes = new FileAttributes();

            Errno error = LookupFileId(path, out short fileId, out bool isDir);
            if(error != Errno.NoError) return error;

            if(!isDir) return GetAttributes(fileId, out attributes);

            attributes = FileAttributes.Directory;

            return Errno.NoError;
        }

        public Errno Read(string path, long offset, long size, ref byte[] buf)
        {
            if(size == 0)
            {
                buf = new byte[0];
                return Errno.NoError;
            }

            if(offset < 0) return Errno.InvalidArgument;

            Errno error = LookupFileId(path, out short fileId, out _);
            if(error != Errno.NoError) return error;

            byte[] tmp;
            if(debug)
                switch(fileId)
                {
                    case FILEID_BOOT_SIGNED:
                    case FILEID_LOADER_SIGNED:
                    case (short)FILEID_MDDF:
                    case (short)FILEID_BITMAP:
                    case (short)FILEID_SRECORD:
                    case (short)FILEID_CATALOG:
                        error = ReadSystemFile(fileId, out tmp);
                        break;
                    default:
                        error = ReadFile(fileId, out tmp);
                        break;
                }
            else error = ReadFile(fileId, out tmp);

            if(error != Errno.NoError) return error;

            if(offset >= tmp.Length) return Errno.EINVAL;

            if(size + offset >= tmp.Length) size = tmp.Length - offset;

            buf = new byte[size];
            Array.Copy(tmp, offset, buf, 0, size);
            return Errno.NoError;
        }

        public Errno Stat(string path, out FileEntryInfo stat)
        {
            stat = null;
            Errno error = LookupFileId(path, out short fileId, out bool isDir);
            if(error != Errno.NoError) return error;

            return isDir ? StatDir(fileId, out stat) : Stat(fileId, out stat);
        }

        Errno GetAttributes(short fileId, out FileAttributes attributes)
        {
            attributes = new FileAttributes();
            if(!mounted) return Errno.AccessDenied;

            if(fileId < 4)
            {
                if(!debug) return Errno.NoSuchFile;

                attributes =  new FileAttributes();
                attributes =  FileAttributes.System;
                attributes |= FileAttributes.Hidden;

                attributes |= FileAttributes.File;

                return Errno.NoError;
            }

            Errno error = ReadExtentsFile(fileId, out ExtentFile extFile);

            if(error != Errno.NoError) return error;

            switch(extFile.ftype)
            {
                case FileType.Spool:
                    attributes |= FileAttributes.CharDevice;
                    break;
                case FileType.UserCat:
                case FileType.RootCat:
                    attributes |= FileAttributes.Directory;
                    break;
                case FileType.Pipe:
                    attributes |= FileAttributes.Pipe;
                    break;
                case FileType.Undefined: break;
                default:
                    attributes |= FileAttributes.File;
                    attributes |= FileAttributes.Extents;
                    break;
            }

            if(extFile.protect        > 0) attributes |= FileAttributes.Immutable;
            if(extFile.locked         > 0) attributes |= FileAttributes.ReadOnly;
            if(extFile.password_valid > 0) attributes |= FileAttributes.Password;

            return Errno.NoError;
        }

        Errno ReadSystemFile(short fileId, out byte[] buf) => ReadSystemFile(fileId, out buf, false);

        Errno ReadSystemFile(short fileId, out byte[] buf, bool tags)
        {
            buf = null;
            if(!mounted || !debug) return Errno.AccessDenied;

            if(fileId > 4 || fileId <= 0)
                if(fileId != FILEID_BOOT_SIGNED && fileId != FILEID_LOADER_SIGNED)
                    return Errno.InvalidArgument;

            if(systemFileCache.TryGetValue(fileId, out buf) && !tags) return Errno.NoError;

            int count = 0;

            if(fileId == FILEID_SRECORD)
                if(!tags)
                {
                    buf = device.ReadSectors(mddf.mddf_block + volumePrefix + mddf.srec_ptr, mddf.srec_len);
                    systemFileCache.Add(fileId, buf);
                    return Errno.NoError;
                }
                else
                {
                    buf = device.ReadSectorsTag(mddf.mddf_block + volumePrefix + mddf.srec_ptr, mddf.srec_len,
                                                SectorTagType.AppleSectorTag);
                    return Errno.NoError;
                }

            LisaTag.PriamTag sysTag;

            // Should be enough to check 100 sectors?
            for(ulong i = 0; i < 100; i++)
            {
                DecodeTag(device.ReadSectorTag(i, SectorTagType.AppleSectorTag), out sysTag);

                if(sysTag.FileId == fileId) count++;
            }

            if(count == 0) return Errno.NoSuchFile;

            buf = !tags ? new byte[count * device.Info.SectorSize] : new byte[count * devTagSize];

            // Should be enough to check 100 sectors?
            for(ulong i = 0; i < 100; i++)
            {
                DecodeTag(device.ReadSectorTag(i, SectorTagType.AppleSectorTag), out sysTag);

                if(sysTag.FileId != fileId) continue;

                byte[] sector = !tags ? device.ReadSector(i) : device.ReadSectorTag(i, SectorTagType.AppleSectorTag);

                // Relative block for $Loader starts at $Boot block
                if(sysTag.FileId == FILEID_LOADER_SIGNED) sysTag.RelPage--;

                Array.Copy(sector, 0, buf, sector.Length * sysTag.RelPage, sector.Length);
            }

            if(!tags) systemFileCache.Add(fileId, buf);

            return Errno.NoError;
        }

        Errno Stat(short fileId, out FileEntryInfo stat)
        {
            stat = null;

            if(!mounted) return Errno.AccessDenied;

            Errno      error;
            ExtentFile file;

            if(fileId <= 4)
                if(!debug || fileId == 0) return Errno.NoSuchFile;
                else
                {
                    stat = new FileEntryInfo {Attributes = new FileAttributes()};

                    error = GetAttributes(fileId, out stat.Attributes);
                    if(error != Errno.NoError) return error;

                    if(fileId < 0 && fileId != FILEID_BOOT_SIGNED && fileId != FILEID_LOADER_SIGNED)
                    {
                        error = ReadExtentsFile((short)(fileId * -1), out file);
                        if(error != Errno.NoError) return error;

                        stat.CreationTime  = DateHandlers.LisaToDateTime(file.dtc);
                        stat.AccessTime    = DateHandlers.LisaToDateTime(file.dta);
                        stat.BackupTime    = DateHandlers.LisaToDateTime(file.dtb);
                        stat.LastWriteTime = DateHandlers.LisaToDateTime(file.dtm);

                        stat.Inode     = (ulong)fileId;
                        stat.Links     = 0;
                        stat.Length    = mddf.datasize;
                        stat.BlockSize = mddf.datasize;
                        stat.Blocks    = 1;
                    }
                    else
                    {
                        error = ReadSystemFile(fileId, out byte[] buf);
                        if(error != Errno.NoError) return error;

                        stat.CreationTime = fileId != 4 ? mddf.dtvc : mddf.dtcc;

                        stat.BackupTime = mddf.dtvb;

                        stat.Inode     = (ulong)fileId;
                        stat.Links     = 0;
                        stat.Length    = buf.Length;
                        stat.BlockSize = mddf.datasize;
                        stat.Blocks    = buf.Length / mddf.datasize;
                    }

                    return Errno.NoError;
                }

            stat  = new FileEntryInfo {Attributes = new FileAttributes()};
            error = GetAttributes(fileId, out stat.Attributes);
            if(error != Errno.NoError) return error;

            error = ReadExtentsFile(fileId, out file);
            if(error != Errno.NoError) return error;

            stat.CreationTime  = DateHandlers.LisaToDateTime(file.dtc);
            stat.AccessTime    = DateHandlers.LisaToDateTime(file.dta);
            stat.BackupTime    = DateHandlers.LisaToDateTime(file.dtb);
            stat.LastWriteTime = DateHandlers.LisaToDateTime(file.dtm);

            stat.Inode = (ulong)fileId;
            stat.Links = 1;
            if(!fileSizeCache.TryGetValue(fileId, out int len)) stat.Length = srecords[fileId].filesize;
            else stat.Length                                                = len;
            stat.BlockSize = mddf.datasize;
            stat.Blocks    = file.length;

            return Errno.NoError;
        }

        Errno ReadFile(short fileId, out byte[] buf) => ReadFile(fileId, out buf, false);

        Errno ReadFile(short fileId, out byte[] buf, bool tags)
        {
            buf = null;
            if(!mounted) return Errno.AccessDenied;

            tags &= debug;

            if(fileId < 4 || fileId == 4 && mddf.fsversion != LISA_V2 && mddf.fsversion != LISA_V1)
                return Errno.InvalidArgument;

            if(!tags && fileCache.TryGetValue(fileId, out buf)) return Errno.NoError;

            Errno error = ReadExtentsFile(fileId, out ExtentFile file);
            if(error != Errno.NoError) return error;

            int sectorSize;
            if(tags) sectorSize = devTagSize;
            else sectorSize     = (int)device.Info.SectorSize;

            byte[] temp = new byte[file.length * sectorSize];

            int offset = 0;
            for(int i = 0; i < file.extents.Length; i++)
            {
                byte[] sector;

                if(!tags)
                    sector = device.ReadSectors((ulong)file.extents[i].start + mddf.mddf_block + volumePrefix,
                                                (uint)file.extents[i].length);
                else
                    sector = device.ReadSectorsTag((ulong)file.extents[i].start + mddf.mddf_block + volumePrefix,
                                                   (uint)file.extents[i].length, SectorTagType.AppleSectorTag);

                Array.Copy(sector, 0, temp, offset, sector.Length);
                offset += sector.Length;
            }

            if(!tags)
            {
                if(fileSizeCache.TryGetValue(fileId, out int realSize))
                    if(realSize > temp.Length)
                        DicConsole.ErrorWriteLine("File {0} gets truncated.", fileId);
                buf = temp;

                fileCache.Add(fileId, buf);
            }
            else buf = temp;

            return Errno.NoError;
        }

        Errno LookupFileId(string path, out short fileId, out bool isDir)
        {
            fileId = 0;
            isDir  = false;

            if(!mounted) return Errno.AccessDenied;

            string[] pathElements = path.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

            if(pathElements.Length == 0)
            {
                fileId = DIRID_ROOT;
                isDir  = true;
                return Errno.NoError;
            }

            // Only V3 supports subdirectories
            if(pathElements.Length > 1 && mddf.fsversion != LISA_V3) return Errno.NotSupported;

            if(debug && pathElements.Length == 1)
            {
                if(string.Compare(pathElements[0], "$MDDF", StringComparison.InvariantCulture) == 0)
                {
                    fileId = (short)FILEID_MDDF;
                    return Errno.NoError;
                }

                if(string.Compare(pathElements[0], "$Boot", StringComparison.InvariantCulture) == 0)
                {
                    fileId = FILEID_BOOT_SIGNED;
                    return Errno.NoError;
                }

                if(string.Compare(pathElements[0], "$Loader", StringComparison.InvariantCulture) == 0)
                {
                    fileId = FILEID_LOADER_SIGNED;
                    return Errno.NoError;
                }

                if(string.Compare(pathElements[0], "$Bitmap", StringComparison.InvariantCulture) == 0)
                {
                    fileId = (short)FILEID_BITMAP;
                    return Errno.NoError;
                }

                if(string.Compare(pathElements[0], "$S-Record", StringComparison.InvariantCulture) == 0)
                {
                    fileId = (short)FILEID_SRECORD;
                    return Errno.NoError;
                }

                if(string.Compare(pathElements[0], "$", StringComparison.InvariantCulture) == 0)
                {
                    fileId = DIRID_ROOT;
                    isDir  = true;
                    return Errno.NoError;
                }
            }

            for(int lvl = 0; lvl < pathElements.Length; lvl++)
            {
                string wantedFilename = pathElements[0].Replace('-', '/');

                foreach(CatalogEntry entry in catalogCache)
                {
                    string filename = StringHandlers.CToString(entry.filename, Encoding);

                    // LisaOS is case insensitive
                    if(string.Compare(wantedFilename, filename, StringComparison.InvariantCultureIgnoreCase) != 0 ||
                       entry.parentID !=
                       fileId) continue;

                    fileId = entry.fileID;
                    isDir  = entry.fileType == 0x01;

                    // Not last path element, and it's not a directory
                    if(lvl != pathElements.Length - 1 && !isDir) return Errno.NotDirectory;

                    // Arrived last path element
                    if(lvl == pathElements.Length - 1) return Errno.NoError;
                }
            }

            return Errno.NoSuchFile;
        }
    }
}