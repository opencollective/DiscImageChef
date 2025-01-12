// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : DiskCopy42.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Disk image plugins.
//
// --[ Description ] ----------------------------------------------------------
//
//     Manages Apple DiskCopy 4.2 disk images, including unofficial modifications.
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

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.CommonTypes.Structs;

namespace DiscImageChef.DiscImages
{
    // Checked using several images and strings inside Apple's DiskImages.framework
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public partial class DiskCopy42 : IWritableImage, IVerifiableImage
    {
        /// <summary>Bytes per tag, should be 12</summary>
        uint bptag;
        /// <summary>Start of data sectors in disk image, should be 0x58</summary>
        uint dataOffset;
        /// <summary>Disk image file</summary>
        IFilter dc42ImageFilter;
        /// <summary>Header of opened image</summary>
        Dc42Header header;
        ImageInfo imageInfo;
        /// <summary>Start of tags in disk image, after data sectors</summary>
        uint tagOffset;
        bool       twiggy;
        byte[]     twiggyCache;
        byte[]     twiggyCacheTags;
        FileStream writingStream;

        public DiskCopy42()
        {
            imageInfo = new ImageInfo
            {
                ReadableSectorTags    = new List<SectorTagType>(),
                ReadableMediaTags     = new List<MediaTagType>(),
                HasPartitions         = false,
                HasSessions           = false,
                Version               = "4.2",
                Application           = "Apple DiskCopy",
                ApplicationVersion    = "4.2",
                Creator               = null,
                Comments              = null,
                MediaManufacturer     = null,
                MediaModel            = null,
                MediaSerialNumber     = null,
                MediaBarcode          = null,
                MediaPartNumber       = null,
                MediaSequence         = 0,
                LastMediaSequence     = 0,
                DriveManufacturer     = null,
                DriveModel            = null,
                DriveSerialNumber     = null,
                DriveFirmwareRevision = null
            };
        }

        ~DiskCopy42()
        {
            Close();
        }
    }
}