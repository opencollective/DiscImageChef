﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Internal.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : ISO9660 filesystem plugin.
//
// --[ Description ] ----------------------------------------------------------
//
//     Internal structures.
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

namespace DiscImageChef.Filesystems.ISO9660
{
    public partial class ISO9660
    {
        struct DecodedVolumeDescriptor
        {
            public string   SystemIdentifier;
            public string   VolumeIdentifier;
            public string   VolumeSetIdentifier;
            public string   PublisherIdentifier;
            public string   DataPreparerIdentifier;
            public string   ApplicationIdentifier;
            public DateTime CreationTime;
            public bool     HasModificationTime;
            public DateTime ModificationTime;
            public bool     HasExpirationTime;
            public DateTime ExpirationTime;
            public bool     HasEffectiveTime;
            public DateTime EffectiveTime;
            public ushort   BlockSize;
            public uint     Blocks;
        }

        class DecodedDirectoryEntry
        {
            public byte[]                         AmigaComment;
            public AmigaProtection?               AmigaProtection;
            public byte?                          AppleDosType;
            public byte[]                         AppleIcon;
            public ushort?                        AppleProDosType;
            public DecodedDirectoryEntry          AssociatedFile;
            public CdiSystemArea?                 CdiSystemArea;
            public List<(uint extent, uint size)> Extents;
            public string                         Filename;
            public byte                           FileUnitSize;
            public FinderInfo                     FinderInfo;
            public FileFlags                      Flags;
            public byte                           Interleave;
            public PosixAttributes?               PosixAttributes;
            public PosixAttributesOld?            PosixAttributesOld;
            public PosixDeviceNumber?             PosixDeviceNumber;
            public DecodedDirectoryEntry          ResourceFork;
            public byte[]                         RockRidgeAlternateName;
            public bool                           RockRidgeRelocated;
            public byte[]                         RripAccess;
            public byte[]                         RripAttributeChange;
            public byte[]                         RripBackup;
            public byte[]                         RripCreation;
            public byte[]                         RripEffective;
            public byte[]                         RripExpiration;
            public byte[]                         RripModify;
            public ulong                          Size;
            public string                         SymbolicLink;
            public DateTime?                      Timestamp;
            public ushort                         VolumeSequenceNumber;
            public CdromXa?                       XA;
            public byte                           XattrLength;

            public override string ToString() => Filename;
        }

        [Flags]
        enum FinderFlags : ushort
        {
            kIsOnDesk            = 0x0001,
            kColor               = 0x000E,
            kRequireSwitchLaunch = 0x0020,
            kIsShared            = 0x0040,
            kHasNoINITs          = 0x0080,
            kHasBeenInited       = 0x0100,
            kHasCustomIcon       = 0x0400,
            kLetter              = 0x0200,
            kChanged             = 0x0200,
            kIsStationery        = 0x0800,
            kNameLocked          = 0x1000,
            kHasBundle           = 0x2000,
            kIsInvisible         = 0x4000,
            kIsAlias             = 0x8000
        }

        struct Point
        {
            public short x;
            public short y;
        }

        class FinderInfo
        {
            public uint        fdCreator;
            public FinderFlags fdFlags;
            public short       fdFldr;
            public Point       fdLocation;
            public uint        fdType;
        }

        class PathTableEntryInternal
        {
            public uint   Extent;
            public string Name;
            public ushort Parent;
            public byte   XattrLength;

            public override string ToString() => Name;
        }
    }
}