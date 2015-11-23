﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Device.cs
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
using System;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using DiscImageChef.Decoders.ATA;

namespace DiscImageChef.Devices
{
    public partial class Device
    {
        /// <summary>
        /// Opens the device for sending direct commands
        /// </summary>
        /// <param name="devicePath">Device path</param>
        public Device(string devicePath)
        {
            platformID = Interop.DetectOS.GetRealPlatformID();
            Timeout = 15;
            error = false;

            switch (platformID)
            {
                case Interop.PlatformID.Win32NT:
                    {
                        fd = Windows.Extern.CreateFile(devicePath,
                            Windows.FileAccess.GenericRead | Windows.FileAccess.GenericWrite,
                            Windows.FileShare.Read | Windows.FileShare.Write,
                            IntPtr.Zero, Windows.FileMode.OpenExisting,
                            Windows.FileAttributes.Normal, IntPtr.Zero);

                        if (((SafeFileHandle)fd).IsInvalid)
                        {
                            error = true;
                            lastError = Marshal.GetLastWin32Error();
                        }

                        //throw new NotImplementedException();
                        break;
                    }
                case Interop.PlatformID.Linux:
                    {
                        fd = Linux.Extern.open(devicePath, Linux.FileFlags.Readonly | Linux.FileFlags.NonBlocking);

                        if ((int)fd < 0)
                        {
                            error = true;
                            lastError = Marshal.GetLastWin32Error();
                        }

                        //throw new NotImplementedException();
                        break;
                    }
                default:
                    throw new InvalidOperationException(String.Format("Platform {0} not yet supported.", platformID));
            }

            type = DeviceType.Unknown;
            scsiType = Decoders.SCSI.PeripheralDeviceTypes.UnknownDevice;

            AtaErrorRegistersCHS errorRegisters;

            byte[] ataBuf;
            byte[] senseBuf;
            byte[] inqBuf;

            bool scsiSense = ScsiInquiry(out inqBuf, out senseBuf);

            if (!scsiSense)
            {
                Decoders.SCSI.Inquiry.SCSIInquiry? Inquiry = Decoders.SCSI.Inquiry.Decode(inqBuf);

                type = DeviceType.SCSI;
                bool sense = ScsiInquiry(out inqBuf, out senseBuf, 0x80);
                if (!sense)
                    serial = Decoders.SCSI.EVPD.DecodePage80(inqBuf);

                if (Inquiry.HasValue)
                {
                    revision = StringHandlers.SpacePaddedToString(Inquiry.Value.ProductRevisionLevel);
                    model = StringHandlers.SpacePaddedToString(Inquiry.Value.ProductIdentification);
                    manufacturer = StringHandlers.SpacePaddedToString(Inquiry.Value.VendorIdentification);

                    scsiType = (Decoders.SCSI.PeripheralDeviceTypes)Inquiry.Value.PeripheralDeviceType;
                }

                sense = AtapiIdentify(out ataBuf, out errorRegisters);

                if (!sense)
                {
                    type = DeviceType.ATAPI;
                    Decoders.ATA.Identify.IdentifyDevice? ATAID = Decoders.ATA.Identify.Decode(ataBuf);

                    if (ATAID.HasValue)
                        serial = ATAID.Value.SerialNumber;
                }
            }

            if (scsiSense || manufacturer == "ATA")
            {
                bool sense = AtaIdentify(out ataBuf, out errorRegisters);
                if (!sense)
                {
                    type = DeviceType.ATA;
                    Decoders.ATA.Identify.IdentifyDevice? ATAID = Decoders.ATA.Identify.Decode(ataBuf);

                    if (ATAID.HasValue)
                    {
                        string[] separated = ATAID.Value.Model.Split(' ');

                        if (separated.Length == 1)
                            model = separated[0];
                        else
                        {
                            manufacturer = separated[0];
                            model = separated[separated.Length - 1];
                        }

                        revision = ATAID.Value.FirmwareRevision;
                        serial = ATAID.Value.SerialNumber;

                        scsiType = Decoders.SCSI.PeripheralDeviceTypes.DirectAccess;
                    }
                }
            }

            if (type == DeviceType.Unknown)
            {
                manufacturer = null;
                model = null;
                revision = null;
                serial = null;
            }
        }
    }
}

