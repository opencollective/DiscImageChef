/***************************************************************************
The Disc Image Chef
----------------------------------------------------------------------------
 
Filename       : UNIXBFS.cs
Version        : 1.0
Author(s)      : Natalia Portillo
 
Component      : Filesystem plugins

Revision       : $Revision$
Last change by : $Author$
Date           : $Date$
 
--[ Description ] ----------------------------------------------------------
 
Identifies UnixWare boot filesystems and shows information.
 
--[ License ] --------------------------------------------------------------
 
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.

----------------------------------------------------------------------------
Copyright (C) 2011-2014 Claunia.com
****************************************************************************/
//$Id$

using System;
using System.Text;
using DiscImageChef;

// Information from the Linux kernel
using DiscImageChef.Console;


namespace DiscImageChef.Plugins
{
    class BFS : Plugin
    {
        const UInt32 BFS_MAGIC = 0x1BADFACE;

        public BFS()
        {
            Name = "UNIX Boot filesystem";
            PluginUUID = new Guid("1E6E0DA6-F7E4-494C-80C6-CB5929E96155");
        }

        public override bool Identify(ImagePlugins.ImagePlugin imagePlugin, ulong partitionStart, ulong partitionEnd)
        {
            if ((2 + partitionStart) >= imagePlugin.GetSectors())
                return false;

            UInt32 magic;

            magic = BitConverter.ToUInt32(imagePlugin.ReadSector(0 + partitionStart), 0);

            return magic == BFS_MAGIC;
        }

        public override void GetInformation(ImagePlugins.ImagePlugin imagePlugin, ulong partitionStart, ulong partitionEnd, out string information)
        {
            information = "";
			
            StringBuilder sb = new StringBuilder();
            byte[] bfs_sb_sector = imagePlugin.ReadSector(0 + partitionStart);
            byte[] sb_strings = new byte[6];

            BFSSuperBlock bfs_sb = new BFSSuperBlock();

            bfs_sb.s_magic = BitConverter.ToUInt32(bfs_sb_sector, 0x00);
            bfs_sb.s_start = BitConverter.ToUInt32(bfs_sb_sector, 0x04);
            bfs_sb.s_end = BitConverter.ToUInt32(bfs_sb_sector, 0x08);
            bfs_sb.s_from = BitConverter.ToUInt32(bfs_sb_sector, 0x0C);
            bfs_sb.s_to = BitConverter.ToUInt32(bfs_sb_sector, 0x10);
            bfs_sb.s_bfrom = BitConverter.ToInt32(bfs_sb_sector, 0x14);
            bfs_sb.s_bto = BitConverter.ToInt32(bfs_sb_sector, 0x18);
            Array.Copy(bfs_sb_sector, 0x1C, sb_strings, 0, 6);
            bfs_sb.s_fsname = StringHandlers.CToString(sb_strings);
            Array.Copy(bfs_sb_sector, 0x22, sb_strings, 0, 6);
            bfs_sb.s_volume = StringHandlers.CToString(sb_strings);

            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_magic: 0x{0:X8}", bfs_sb.s_magic);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_start: 0x{0:X8}", bfs_sb.s_start);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_end: 0x{0:X8}", bfs_sb.s_end);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_from: 0x{0:X8}", bfs_sb.s_from);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_to: 0x{0:X8}", bfs_sb.s_to);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_bfrom: 0x{0:X8}", bfs_sb.s_bfrom);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_bto: 0x{0:X8}", bfs_sb.s_bto);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_fsname: 0x{0}", bfs_sb.s_fsname);
            DicConsole.DebugWriteLine("BFS plugin", "bfs_sb.s_volume: 0x{0}", bfs_sb.s_volume);

            sb.AppendLine("UNIX Boot filesystem");
            sb.AppendFormat("Volume goes from byte {0} to byte {1}, for {2} bytes", bfs_sb.s_start, bfs_sb.s_end, bfs_sb.s_end - bfs_sb.s_start).AppendLine();
            sb.AppendFormat("Filesystem name: {0}", bfs_sb.s_fsname).AppendLine();
            sb.AppendFormat("Volume name: {0}", bfs_sb.s_volume).AppendLine();

            information = sb.ToString();
        }

        struct BFSSuperBlock
        {
            public UInt32 s_magic;
            // 0x00, 0x1BADFACE
            public UInt32 s_start;
            // 0x04, start in bytes of volume
            public UInt32 s_end;
            // 0x08, end in bytes of volume
            public UInt32 s_from;
            // 0x0C, unknown :p
            public UInt32 s_to;
            // 0x10, unknown :p
            public Int32 s_bfrom;
            // 0x14, unknown :p
            public Int32 s_bto;
            // 0x18, unknown :p
            public string s_fsname;
            // 0x1C, 6 bytes, filesystem name
            public string s_volume;
            // 0x22, 6 bytes, volume name
        }
    }
}