﻿// /***************************************************************************
// The Disc Image Chef
// ----------------------------------------------------------------------------
//
// Filename       : ListOptions.cs
// Author(s)      : Natalia Portillo <claunia@claunia.com>
//
// Component      : Verbs.
//
// --[ Description ] ----------------------------------------------------------
//
//     Lists all options supported by read-only filesystems.
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

using System;
using System.Collections.Generic;
using System.Linq;
using DiscImageChef.CommonTypes;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.Console;
using DiscImageChef.Core;
using Mono.Options;

namespace DiscImageChef.Commands
{
    class ListOptionsCommand : Command
    {
        bool showHelp;

        public ListOptionsCommand() : base("list-options",
                                           "Lists all options supported by read-only filesystems and writable media images.")
        {
            Options = new OptionSet
            {
                $"{MainClass.AssemblyTitle} {MainClass.AssemblyVersion?.InformationalVersion}",
                $"{MainClass.AssemblyCopyright}",
                "",
                $"usage: DiscImageChef {Name}",
                "",
                Help,
                {"help|h|?", "Show this message and exit.", v => showHelp = v != null}
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

            if(extra.Count > 0)
            {
                DicConsole.ErrorWriteLine("Too many arguments.");
                return (int)ErrorNumber.UnexpectedArgumentCount;
            }

            DicConsole.DebugWriteLine("List-Options command", "--debug={0}",   MainClass.Debug);
            DicConsole.DebugWriteLine("List-Options command", "--verbose={0}", MainClass.Verbose);
            Statistics.AddCommand("list-options");

            PluginBase plugins = GetPluginBase.Instance;

            DicConsole.WriteLine("Read-only filesystems options:");
            foreach(KeyValuePair<string, IReadOnlyFilesystem> kvp in plugins.ReadOnlyFilesystems)
            {
                List<(string name, Type type, string description)> options = kvp.Value.SupportedOptions.ToList();
                if(options.Count == 0) continue;

                DicConsole.WriteLine("\tOptions for {0}:",         kvp.Value.Name);
                DicConsole.WriteLine("\t\t{0,-16} {1,-16} {2,-8}", "Name", "Type", "Description");
                foreach((string name, Type type, string description) option in options.OrderBy(t => t.name))
                    DicConsole.WriteLine("\t\t{0,-16} {1,-16} {2,-8}", option.name, TypeToString(option.type),
                                         option.description);
                DicConsole.WriteLine();
            }

            DicConsole.WriteLine();

            DicConsole.WriteLine("Read/Write media images options:");
            foreach(KeyValuePair<string, IWritableImage> kvp in plugins.WritableImages)
            {
                List<(string name, Type type, string description, object @default)> options =
                    kvp.Value.SupportedOptions.ToList();
                if(options.Count == 0) continue;

                DicConsole.WriteLine("\tOptions for {0}:",                 kvp.Value.Name);
                DicConsole.WriteLine("\t\t{0,-20} {1,-10} {2,-12} {3,-8}", "Name", "Type", "Default", "Description");
                foreach((string name, Type type, string description, object @default) option in
                    options.OrderBy(t => t.name))
                    DicConsole.WriteLine("\t\t{0,-20} {1,-10} {2,-12} {3,-8}", option.name, TypeToString(option.type),
                                         option.@default, option.description);
                DicConsole.WriteLine();
            }

            return (int)ErrorNumber.NoError;
        }

        static string TypeToString(Type type)
        {
            if(type == typeof(bool)) return "boolean";

            if(type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long))
                return "signed number";

            if(type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong))
                return "number";

            if(type == typeof(float) || type == typeof(double)) return "float number";

            if(type == typeof(Guid)) return "uuid";

            return type == typeof(string) ? "string" : type.ToString();
        }
    }
}