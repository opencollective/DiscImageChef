﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Properties.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Disk image plugins.
//
// --[ Description ] ----------------------------------------------------------
//
//     Contains properties for DiscJuggler disc images.
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
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Structs;
using Schemas;

namespace DiscImageChef.DiscImages
{
    public partial class DiscJuggler
    {
        public string                 Name         => "DiscJuggler";
        public Guid                   Id           => new Guid("2444DBC6-CD35-424C-A227-39B0C4DB01B2");
        public ImageInfo              Info         => imageInfo;
        public string                 Format       => "DiscJuggler";
        public string                 Author       => "Natalia Portillo";
        public List<Partition>        Partitions   { get; private set; }
        public List<Track>            Tracks       { get; private set; }
        public List<Session>          Sessions     { get; private set; }
        public List<DumpHardwareType> DumpHardware => null;
        public CICMMetadataType       CicmMetadata => null;
    }
}