﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : BTRFS.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : B-tree file system plugin.
//
// --[ Description ] ----------------------------------------------------------
//
//     Identifies the B-tree file system and shows information.
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
using System.Runtime.InteropServices;
using System.Text;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.Console;
using Schemas;
using Marshal = DiscImageChef.Helpers.Marshal;

namespace DiscImageChef.Filesystems
{
    public class BTRFS : IFilesystem
    {
        /// <summary>
        ///     BTRFS magic "_BHRfS_M"
        /// </summary>
        const ulong BTRFS_MAGIC = 0x4D5F53665248425F;

        public FileSystemType XmlFsType { get; private set; }
        public Encoding       Encoding  { get; private set; }
        public string         Name      => "B-tree file system";
        public Guid           Id        => new Guid("C904CF15-5222-446B-B7DB-02EAC5D781B3");
        public string         Author    => "Natalia Portillo";

        public bool Identify(IMediaImage imagePlugin, Partition partition)
        {
            if(partition.Start >= partition.End) return false;

            ulong sbSectorOff  = 0x10000 / imagePlugin.Info.SectorSize;
            uint  sbSectorSize = 0x1000  / imagePlugin.Info.SectorSize;

            if(sbSectorOff + partition.Start >= partition.End) return false;

            byte[]     sector = imagePlugin.ReadSectors(sbSectorOff + partition.Start, sbSectorSize);
            SuperBlock btrfsSb;

            try { btrfsSb = Marshal.ByteArrayToStructureLittleEndian<SuperBlock>(sector); }
            catch { return false; }

            DicConsole.DebugWriteLine("BTRFS Plugin", "sbSectorOff = {0}",                    sbSectorOff);
            DicConsole.DebugWriteLine("BTRFS Plugin", "sbSectorSize = {0}",                   sbSectorSize);
            DicConsole.DebugWriteLine("BTRFS Plugin", "partition.PartitionStartSector = {0}", partition.Start);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.magic = 0x{0:X16}",            btrfsSb.magic);

            return btrfsSb.magic == BTRFS_MAGIC;
        }

        public void GetInformation(IMediaImage imagePlugin, Partition partition, out string information,
                                   Encoding    encoding)
        {
            Encoding = encoding ?? Encoding.GetEncoding("iso-8859-15");
            StringBuilder sbInformation = new StringBuilder();
            XmlFsType   = new FileSystemType();
            information = "";

            ulong sbSectorOff  = 0x10000 / imagePlugin.Info.SectorSize;
            uint  sbSectorSize = 0x1000  / imagePlugin.Info.SectorSize;

            byte[] sector = imagePlugin.ReadSectors(sbSectorOff + partition.Start, sbSectorSize);

            SuperBlock btrfsSb = Marshal.ByteArrayToStructureLittleEndian<SuperBlock>(sector);

            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.checksum = {0}",          btrfsSb.checksum);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.uuid = {0}",              btrfsSb.uuid);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.pba = {0}",               btrfsSb.pba);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.flags = {0}",             btrfsSb.flags);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.magic = {0}",             btrfsSb.magic);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.generation = {0}",        btrfsSb.generation);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.root_lba = {0}",          btrfsSb.root_lba);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.chunk_lba = {0}",         btrfsSb.chunk_lba);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.log_lba = {0}",           btrfsSb.log_lba);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.log_root_transid = {0}",  btrfsSb.log_root_transid);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.total_bytes = {0}",       btrfsSb.total_bytes);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.bytes_used = {0}",        btrfsSb.bytes_used);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.root_dir_objectid = {0}", btrfsSb.root_dir_objectid);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.num_devices = {0}",       btrfsSb.num_devices);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.sectorsize = {0}",        btrfsSb.sectorsize);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.nodesize = {0}",          btrfsSb.nodesize);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.leafsize = {0}",          btrfsSb.leafsize);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.stripesize = {0}",        btrfsSb.stripesize);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.n = {0}",                 btrfsSb.n);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.chunk_root_generation = {0}",
                                      btrfsSb.chunk_root_generation);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.compat_flags = 0x{0:X16}",    btrfsSb.compat_flags);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.compat_ro_flags = 0x{0:X16}", btrfsSb.compat_ro_flags);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.incompat_flags = 0x{0:X16}",  btrfsSb.incompat_flags);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.csum_type = {0}",             btrfsSb.csum_type);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.root_level = {0}",            btrfsSb.root_level);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.chunk_root_level = {0}",      btrfsSb.chunk_root_level);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.log_root_level = {0}",        btrfsSb.log_root_level);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.id = 0x{0:X16}",     btrfsSb.dev_item.id);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.bytes = {0}",        btrfsSb.dev_item.bytes);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.used = {0}",         btrfsSb.dev_item.used);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.optimal_align = {0}",
                                      btrfsSb.dev_item.optimal_align);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.optimal_width = {0}",
                                      btrfsSb.dev_item.optimal_width);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.minimal_size = {0}",
                                      btrfsSb.dev_item.minimal_size);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.type = {0}",       btrfsSb.dev_item.type);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.generation = {0}", btrfsSb.dev_item.generation);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.start_offset = {0}",
                                      btrfsSb.dev_item.start_offset);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.dev_group = {0}",  btrfsSb.dev_item.dev_group);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.seek_speed = {0}", btrfsSb.dev_item.seek_speed);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.bandwitdh = {0}",  btrfsSb.dev_item.bandwitdh);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.device_uuid = {0}",
                                      btrfsSb.dev_item.device_uuid);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.dev_item.uuid = {0}", btrfsSb.dev_item.uuid);
            DicConsole.DebugWriteLine("BTRFS Plugin", "btrfsSb.label = {0}",         btrfsSb.label);

            sbInformation.AppendLine("B-tree filesystem");
            sbInformation.AppendFormat("UUID: {0}", btrfsSb.uuid).AppendLine();
            sbInformation.AppendFormat("This superblock resides on physical block {0}", btrfsSb.pba).AppendLine();
            sbInformation.AppendFormat("Root tree starts at LBA {0}", btrfsSb.root_lba).AppendLine();
            sbInformation.AppendFormat("Chunk tree starts at LBA {0}", btrfsSb.chunk_lba).AppendLine();
            sbInformation.AppendFormat("Log tree starts at LBA {0}", btrfsSb.log_lba).AppendLine();
            sbInformation.AppendFormat("Volume has {0} bytes spanned in {1} devices", btrfsSb.total_bytes,
                                       btrfsSb.num_devices).AppendLine();
            sbInformation.AppendFormat("Volume has {0} bytes used", btrfsSb.bytes_used).AppendLine();
            sbInformation.AppendFormat("{0} bytes/sector", btrfsSb.sectorsize).AppendLine();
            sbInformation.AppendFormat("{0} bytes/node", btrfsSb.nodesize).AppendLine();
            sbInformation.AppendFormat("{0} bytes/leaf", btrfsSb.leafsize).AppendLine();
            sbInformation.AppendFormat("{0} bytes/stripe", btrfsSb.stripesize).AppendLine();
            sbInformation.AppendFormat("Flags: 0x{0:X}", btrfsSb.flags).AppendLine();
            sbInformation.AppendFormat("Compatible flags: 0x{0:X}", btrfsSb.compat_flags).AppendLine();
            sbInformation.AppendFormat("Read-only compatible flags: 0x{0:X}", btrfsSb.compat_ro_flags).AppendLine();
            sbInformation.AppendFormat("Incompatible flags: 0x{0:X}", btrfsSb.incompat_flags).AppendLine();
            sbInformation.AppendFormat("Device's UUID: {0}", btrfsSb.dev_item.uuid).AppendLine();
            sbInformation.AppendFormat("Volume label: {0}", btrfsSb.label).AppendLine();

            information = sbInformation.ToString();

            XmlFsType = new FileSystemType
            {
                Clusters              = btrfsSb.total_bytes / btrfsSb.sectorsize,
                ClusterSize           = btrfsSb.sectorsize,
                FreeClustersSpecified = true,
                VolumeName            = btrfsSb.label,
                VolumeSerial          = $"{btrfsSb.uuid}",
                VolumeSetIdentifier   = $"{btrfsSb.dev_item.device_uuid}",
                Type                  = Name
            };
            XmlFsType.FreeClusters = XmlFsType.Clusters - btrfsSb.bytes_used / btrfsSb.sectorsize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct SuperBlock
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x20)]
            public readonly byte[] checksum;
            public readonly Guid    uuid;
            public readonly ulong   pba;
            public readonly ulong   flags;
            public readonly ulong   magic;
            public readonly ulong   generation;
            public readonly ulong   root_lba;
            public readonly ulong   chunk_lba;
            public readonly ulong   log_lba;
            public readonly ulong   log_root_transid;
            public readonly ulong   total_bytes;
            public readonly ulong   bytes_used;
            public readonly ulong   root_dir_objectid;
            public readonly ulong   num_devices;
            public readonly uint    sectorsize;
            public readonly uint    nodesize;
            public readonly uint    leafsize;
            public readonly uint    stripesize;
            public readonly uint    n;
            public readonly ulong   chunk_root_generation;
            public readonly ulong   compat_flags;
            public readonly ulong   compat_ro_flags;
            public readonly ulong   incompat_flags;
            public readonly ushort  csum_type;
            public readonly byte    root_level;
            public readonly byte    chunk_root_level;
            public readonly byte    log_root_level;
            public readonly DevItem dev_item;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 0x100)]
            public readonly string label;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x100)]
            public readonly byte[] reserved;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x800)]
            public readonly byte[] chunkpairs;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x4D5)]
            public readonly byte[] unused;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct DevItem
        {
            public readonly ulong id;
            public readonly ulong bytes;
            public readonly ulong used;
            public readonly uint  optimal_align;
            public readonly uint  optimal_width;
            public readonly uint  minimal_size;
            public readonly ulong type;
            public readonly ulong generation;
            public readonly ulong start_offset;
            public readonly uint  dev_group;
            public readonly byte  seek_speed;
            public readonly byte  bandwitdh;
            public readonly Guid  device_uuid;
            public readonly Guid  uuid;
        }
    }
}