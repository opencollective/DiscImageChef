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
//     Contains properties for Ray Arachelian's disk images.
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
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Structs;
using Schemas;

namespace DiscImageChef.DiscImages
{
    public partial class RayDim
    {
        public string                     Name                => "Ray Arachelian's Disk IMage";
        public Guid                       Id                  => new Guid("F541F4E7-C1E3-4A2D-B07F-D863E87AB961");
        public ImageInfo                  Info                => imageInfo;
        public string                     Author              => "Natalia Portillo";
        public string                     Format              => "Ray Arachelian's Disk IMage";
        public List<DumpHardwareType>     DumpHardware        => null;
        public CICMMetadataType           CicmMetadata        => null;
        public IEnumerable<MediaTagType>  SupportedMediaTags  => new MediaTagType[] { };
        public IEnumerable<SectorTagType> SupportedSectorTags => new SectorTagType[] { };
        // TODO: Test with real hardware to see real supported media
        public IEnumerable<MediaType> SupportedMediaTypes =>
            new[]
            {
                MediaType.Apricot_35, MediaType.ATARI_35_DS_DD, MediaType.ATARI_35_DS_DD_11,
                MediaType.ATARI_35_SS_DD, MediaType.ATARI_35_SS_DD_11, MediaType.DMF, MediaType.DMF_82,
                MediaType.DOS_35_DS_DD_8, MediaType.DOS_35_DS_DD_9, MediaType.DOS_35_ED, MediaType.DOS_35_HD,
                MediaType.DOS_35_SS_DD_8, MediaType.DOS_35_SS_DD_9, MediaType.DOS_525_DS_DD_8,
                MediaType.DOS_525_DS_DD_9, MediaType.DOS_525_HD, MediaType.DOS_525_SS_DD_8,
                MediaType.DOS_525_SS_DD_9, MediaType.FDFORMAT_35_DD, MediaType.FDFORMAT_35_HD,
                MediaType.FDFORMAT_525_DD, MediaType.FDFORMAT_525_HD, MediaType.RX50, MediaType.XDF_35,
                MediaType.XDF_525
            };
        public IEnumerable<(string name, Type type, string description, object @default)> SupportedOptions =>
            new (string name, Type type, string description, object @default)[] { };
        public IEnumerable<string> KnownExtensions => new[] {".dim"};
        public bool                IsWriting       { get; private set; }
        public string              ErrorMessage    { get; private set; }
    }
}