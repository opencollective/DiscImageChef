﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : GZip.cs
// Version        : 1.0
// Author(s)      : Natalia Portillo
//
// Component      : Component
//
// Revision       : $Revision$
// Last change by : $Author$
// Date           : $Date$
//
// --[ Description ] ----------------------------------------------------------
//
// Description
//
// --[ License ] --------------------------------------------------------------
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as
//     published by the Free Software Foundation, either version 3 of the
//     License, or (at your option) any later version.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
// ----------------------------------------------------------------------------
// Copyright (C) 2011-2015 Claunia.com
// ****************************************************************************/
// //$Id$
using System.IO;
using DiscImageChef.Checksums;
using DiscImageChef.Filters;
using NUnit.Framework;

namespace DiscImageChef.Tests.Filters
{
    [TestFixture]
    public class XZ
    {
        static readonly byte[] ExpectedFile = { 0x6c, 0x88, 0xa5, 0x9a, 0x1b, 0x7a, 0xec, 0x59, 0x2b, 0xef, 0x8a, 0x28, 0xdb, 0x11, 0x01, 0xc8 };
        static readonly byte[] ExpectedContents = { 0x18, 0x90, 0x5a, 0xf9, 0x83, 0xd8, 0x2b, 0xdd, 0x1a, 0xcc, 0x69, 0x75, 0x4f, 0x0f, 0x81, 0x5e };
        readonly string location;

        public XZ()
        {
            location = Path.Combine(Consts.TestFilesRoot, "filters", "xz.xz");
        }

        [Test]
        public void CheckCorrectFile()
        {
            MD5Context ctx = new MD5Context();
            ctx.Init();
            byte[] result = ctx.File(location);
            Assert.AreEqual(ExpectedFile, result);
        }

        [Test]
        public void CheckFilterId()
        {
            Filter filter = new DiscImageChef.Filters.XZ();
            Assert.AreEqual(true, filter.Identify(location));
        }

        [Test]
        public void Test()
        {
            Filter filter = new DiscImageChef.Filters.XZ();
            filter.Open(location);
            Assert.AreEqual(true, filter.IsOpened());
            Assert.AreEqual(1048576, filter.GetDataForkLength());
            Assert.AreNotEqual(null, filter.GetDataForkStream());
            Assert.AreEqual(0, filter.GetResourceForkLength());
            Assert.AreEqual(null, filter.GetResourceForkStream());
            Assert.AreEqual(false, filter.HasResourceFork());
            filter.Close();
        }

        [Test]
        public void CheckContents()
        {
            Filter filter = new DiscImageChef.Filters.XZ();
            filter.Open(location);
            Stream str = filter.GetDataForkStream();
            byte[] data = new byte[1048576];
            str.Read(data, 0, 1048576);
            str.Close();
            str.Dispose();
            filter.Close();
            MD5Context ctx = new MD5Context();
            ctx.Init();
            ctx.Data(data, out byte[] result);
            Assert.AreEqual(ExpectedContents, result);
        }
    }
}