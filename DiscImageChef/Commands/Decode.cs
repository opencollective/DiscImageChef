// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : Decode.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Verbs.
//
// --[ Description ] ----------------------------------------------------------
//
//     Implements the 'decode' verb.
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
// Copyright © 2011-2019 Natalia Portillo
// ****************************************************************************/

using System.Collections.Generic;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.Console;
using DiscImageChef.Core;
using DiscImageChef.Decoders.ATA;
using DiscImageChef.Decoders.CD;
using DiscImageChef.Decoders.SCSI;
using Mono.Options;

namespace DiscImageChef.Commands
{
    class DecodeCommand : Command
    {
        bool   diskTags = true;
        string inputFile;
        string length     = "all";
        bool   sectorTags = true;
        bool   showHelp;
        ulong  startSector;

        public DecodeCommand() : base("decode", "Decodes and pretty prints disk and/or sector tags.")
        {
            Options = new OptionSet
            {
                $"{MainClass.AssemblyTitle} {MainClass.AssemblyVersion?.InformationalVersion}",
                $"{MainClass.AssemblyCopyright}",
                "",
                $"usage: DiscImageChef {Name} [OPTIONS] imagefile",
                "",
                Help,
                {"disk-tags|f", "Decode disk tags.", b => diskTags                           = b != null},
                {"length|l=", "How many sectors to decode, or \"all\".", s => length         = s},
                {"sector-tags|p", "Decode sector tags.", b => sectorTags                     = b != null},
                {"start|s=", "Name of character encoding to use.", (ulong ul) => startSector = ul},
                {"help|h|?", "Show this message and exit.", v => showHelp                    = v != null}
            };
        }

        public override int Invoke(IEnumerable<string> arguments)
        {
            List<string> extra = Options.Parse(arguments);

            if(showHelp)
            {
                Options.WriteOptionDescriptions(CommandSet.Out);
                return (int)ErrorNumber.HelpRequested;
            }

            MainClass.PrintCopyright();
            if(MainClass.Debug) DicConsole.DebugWriteLineEvent     += System.Console.Error.WriteLine;
            if(MainClass.Verbose) DicConsole.VerboseWriteLineEvent += System.Console.WriteLine;
            Statistics.AddCommand("decode");

            if(extra.Count > 1)
            {
                DicConsole.ErrorWriteLine("Too many arguments.");
                return (int)ErrorNumber.UnexpectedArgumentCount;
            }

            if(extra.Count == 0)
            {
                DicConsole.ErrorWriteLine("Missing input image.");
                return (int)ErrorNumber.MissingArgument;
            }

            inputFile = extra[0];

            DicConsole.DebugWriteLine("Decode command", "--debug={0}",       MainClass.Debug);
            DicConsole.DebugWriteLine("Decode command", "--disk-tags={0}",   diskTags);
            DicConsole.DebugWriteLine("Decode command", "--input={0}",       inputFile);
            DicConsole.DebugWriteLine("Decode command", "--length={0}",      length);
            DicConsole.DebugWriteLine("Decode command", "--sector-tags={0}", sectorTags);
            DicConsole.DebugWriteLine("Decode command", "--start={0}",       startSector);
            DicConsole.DebugWriteLine("Decode command", "--verbose={0}",     MainClass.Verbose);

            FiltersList filtersList = new FiltersList();
            IFilter     inputFilter = filtersList.GetFilter(inputFile);

            if(inputFilter == null)
            {
                DicConsole.ErrorWriteLine("Cannot open specified file.");
                return (int)ErrorNumber.CannotOpenFile;
            }

            IMediaImage inputFormat = ImageFormat.Detect(inputFilter);

            if(inputFormat == null)
            {
                DicConsole.ErrorWriteLine("Unable to recognize image format, not decoding");
                return (int)ErrorNumber.UnrecognizedFormat;
            }

            inputFormat.Open(inputFilter);
            Statistics.AddMediaFormat(inputFormat.Format);
            Statistics.AddMedia(inputFormat.Info.MediaType, false);
            Statistics.AddFilter(inputFilter.Name);

            if(diskTags)
                if(inputFormat.Info.ReadableMediaTags.Count == 0)
                    DicConsole.WriteLine("There are no disk tags in chosen disc image.");
                else
                    foreach(MediaTagType tag in inputFormat.Info.ReadableMediaTags)
                        switch(tag)
                        {
                            case MediaTagType.SCSI_INQUIRY:
                            {
                                byte[] inquiry = inputFormat.ReadDiskTag(MediaTagType.SCSI_INQUIRY);
                                if(inquiry == null)
                                    DicConsole.WriteLine("Error reading SCSI INQUIRY response from disc image");
                                else
                                {
                                    DicConsole.WriteLine("SCSI INQUIRY command response:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(Inquiry.Prettify(inquiry));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.ATA_IDENTIFY:
                            {
                                byte[] identify = inputFormat.ReadDiskTag(MediaTagType.ATA_IDENTIFY);
                                if(identify == null)
                                    DicConsole.WriteLine("Error reading ATA IDENTIFY DEVICE response from disc image");
                                else
                                {
                                    DicConsole.WriteLine("ATA IDENTIFY DEVICE command response:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(Identify.Prettify(identify));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.ATAPI_IDENTIFY:
                            {
                                byte[] identify = inputFormat.ReadDiskTag(MediaTagType.ATAPI_IDENTIFY);
                                if(identify == null)
                                    DicConsole
                                       .WriteLine("Error reading ATA IDENTIFY PACKET DEVICE response from disc image");
                                else
                                {
                                    DicConsole.WriteLine("ATA IDENTIFY PACKET DEVICE command response:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(Identify.Prettify(identify));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.CD_ATIP:
                            {
                                byte[] atip = inputFormat.ReadDiskTag(MediaTagType.CD_ATIP);
                                if(atip == null) DicConsole.WriteLine("Error reading CD ATIP from disc image");
                                else
                                {
                                    DicConsole.WriteLine("CD ATIP:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(ATIP.Prettify(atip));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.CD_FullTOC:
                            {
                                byte[] fulltoc = inputFormat.ReadDiskTag(MediaTagType.CD_FullTOC);
                                if(fulltoc == null) DicConsole.WriteLine("Error reading CD full TOC from disc image");
                                else
                                {
                                    DicConsole.WriteLine("CD full TOC:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(FullTOC.Prettify(fulltoc));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.CD_PMA:
                            {
                                byte[] pma = inputFormat.ReadDiskTag(MediaTagType.CD_PMA);
                                if(pma == null) DicConsole.WriteLine("Error reading CD PMA from disc image");
                                else
                                {
                                    DicConsole.WriteLine("CD PMA:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(PMA.Prettify(pma));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.CD_SessionInfo:
                            {
                                byte[] sessioninfo = inputFormat.ReadDiskTag(MediaTagType.CD_SessionInfo);
                                if(sessioninfo == null)
                                    DicConsole.WriteLine("Error reading CD session information from disc image");
                                else
                                {
                                    DicConsole.WriteLine("CD session information:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(Session.Prettify(sessioninfo));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.CD_TEXT:
                            {
                                byte[] cdtext = inputFormat.ReadDiskTag(MediaTagType.CD_TEXT);
                                if(cdtext == null) DicConsole.WriteLine("Error reading CD-TEXT from disc image");
                                else
                                {
                                    DicConsole.WriteLine("CD-TEXT:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(CDTextOnLeadIn.Prettify(cdtext));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            case MediaTagType.CD_TOC:
                            {
                                byte[] toc = inputFormat.ReadDiskTag(MediaTagType.CD_TOC);
                                if(toc == null) DicConsole.WriteLine("Error reading CD TOC from disc image");
                                else
                                {
                                    DicConsole.WriteLine("CD TOC:");
                                    DicConsole
                                       .WriteLine("================================================================================");
                                    DicConsole.WriteLine(TOC.Prettify(toc));
                                    DicConsole
                                       .WriteLine("================================================================================");
                                }

                                break;
                            }
                            default:
                                DicConsole.WriteLine("Decoder for disk tag type \"{0}\" not yet implemented, sorry.",
                                                     tag);
                                break;
                        }

            if(sectorTags)
            {
                if(length.ToLowerInvariant() == "all") { }
                else
                {
                    if(!ulong.TryParse(length, out ulong _))
                    {
                        DicConsole.WriteLine("Value \"{0}\" is not a valid number for length.", length);
                        DicConsole.WriteLine("Not decoding sectors tags");
                        return 3;
                    }
                }

                if(inputFormat.Info.ReadableSectorTags.Count == 0)
                    DicConsole.WriteLine("There are no sector tags in chosen disc image.");
                else
                    foreach(SectorTagType tag in inputFormat.Info.ReadableSectorTags)
                        switch(tag)
                        {
                            default:
                                DicConsole.WriteLine("Decoder for disk tag type \"{0}\" not yet implemented, sorry.",
                                                     tag);
                                break;
                        }

                // TODO: Not implemented
            }

            return (int)ErrorNumber.NoError;
        }
    }
}