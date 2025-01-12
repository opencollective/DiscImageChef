using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using DiscImageChef.CommonTypes.Enums;
using DiscImageChef.CommonTypes.Interfaces;
using DiscImageChef.CommonTypes.Metadata;
using DiscImageChef.Core.Logging;
using DiscImageChef.Devices;
using Schemas;

namespace DiscImageChef.Core.Devices.Dumping
{
    public partial class Dump
    {
        readonly Device                     dev;
        readonly string                     devicePath;
        readonly bool                       doResume;
        readonly DumpLog                    dumpLog;
        readonly bool                       dumpRaw;
        readonly Encoding                   encoding;
        readonly bool                       force;
        readonly Dictionary<string, string> formatOptions;
        readonly bool                       nometadata;
        readonly bool                       notrim;
        readonly string                     outputPath;
        readonly IWritableImage             outputPlugin;
        readonly string                     outputPrefix;
        readonly bool                       persistent;
        readonly CICMMetadataType           preSidecar;
        readonly ushort                     retryPasses;
        readonly bool                       stopOnError;
        bool                                aborted;
        bool                                dumpFirstTrackPregap;
        Resume                              resume;
        Sidecar                             sidecarClass;
        uint                                skip;

        /// <summary>
        ///     Initializes dumpers
        /// </summary>
        /// <param name="doResume">Should resume?</param>
        /// <param name="dev">Device</param>
        /// <param name="devicePath">Path to the device</param>
        /// <param name="outputPrefix">Prefix for output log files</param>
        /// <param name="outputPlugin">Plugin for output file</param>
        /// <param name="retryPasses">How many times to retry</param>
        /// <param name="force">Force to continue dump whenever possible</param>
        /// <param name="dumpRaw">Dump long sectors</param>
        /// <param name="persistent">Store whatever data the drive returned on error</param>
        /// <param name="stopOnError">Stop dump on first error</param>
        /// <param name="resume">Information for dump resuming</param>
        /// <param name="dumpLog">Dump logger</param>
        /// <param name="encoding">Encoding to use when analyzing dump</param>
        /// <param name="outputPath">Path to output file</param>
        /// <param name="formatOptions">Formats to pass to output file plugin</param>
        /// <param name="notrim">Do not trim errors from skipped sectors</param>
        /// <param name="dumpFirstTrackPregap">Try to read and dump as much first track pregap as possible</param>
        /// <param name="preSidecar">Sidecar to store in dumped image</param>
        /// <param name="skip">How many sectors to skip reading on error</param>
        /// <param name="nometadata">Create metadata sidecar after dump?</param>
        public Dump(bool                       doResume,     Device dev, string devicePath,
                    IWritableImage             outputPlugin, ushort retryPasses,
                    bool                       force,        bool   dumpRaw,      bool    persistent,
                    bool                       stopOnError,  Resume resume,       DumpLog dumpLog,
                    Encoding                   encoding,     string outputPrefix, string  outputPath,
                    Dictionary<string, string> formatOptions,
                    CICMMetadataType           preSidecar, uint skip, bool nometadata,
                    bool                       notrim,     bool dumpFirstTrackPregap)
        {
            this.doResume             = doResume;
            this.dev                  = dev;
            this.devicePath           = devicePath;
            this.outputPlugin         = outputPlugin;
            this.retryPasses          = retryPasses;
            this.force                = force;
            this.dumpRaw              = dumpRaw;
            this.persistent           = persistent;
            this.stopOnError          = stopOnError;
            this.resume               = resume;
            this.dumpLog              = dumpLog;
            this.encoding             = encoding;
            this.outputPrefix         = outputPrefix;
            this.outputPath           = outputPath;
            this.formatOptions        = formatOptions;
            this.preSidecar           = preSidecar;
            this.skip                 = skip;
            this.nometadata           = nometadata;
            this.notrim               = notrim;
            this.dumpFirstTrackPregap = dumpFirstTrackPregap;
            aborted                   = false;
        }

        /// <summary>
        ///     Starts dumping with the stablished fields and autodetecting the device type
        /// </summary>
        public void Start()
        {
            if(dev.IsUsb && dev.UsbVendorId == 0x054C &&
               (dev.UsbProductId == 0x01C8 || dev.UsbProductId == 0x01C9 || dev.UsbProductId == 0x02D2))
                PlayStationPortable();
            else
                switch(dev.Type)
                {
                    case DeviceType.ATA:
                        Ata();
                        break;
                    case DeviceType.MMC:
                    case DeviceType.SecureDigital:
                        SecureDigital();
                        break;
                    case DeviceType.NVMe:
                        NVMe();
                        break;
                    case DeviceType.ATAPI:
                    case DeviceType.SCSI:
                        Scsi();
                        break;
                    default:
                        dumpLog.WriteLine("Unknown device type.");
                        dumpLog.Close();
                        StoppingErrorMessage?.Invoke("Unknown device type.");
                        return;
                }

            dumpLog.Close();

            if(resume == null || !doResume) return;

            resume.LastWriteDate = DateTime.UtcNow;
            resume.BadBlocks.Sort();

            if(File.Exists(outputPrefix + ".resume.xml")) File.Delete(outputPrefix + ".resume.xml");

            FileStream    fs = new FileStream(outputPrefix + ".resume.xml", FileMode.Create, FileAccess.ReadWrite);
            XmlSerializer xs = new XmlSerializer(resume.GetType());
            xs.Serialize(fs, resume);
            fs.Close();
        }

        public void Abort()
        {
            aborted = true;
            sidecarClass?.Abort();
        }

        /// <summary>
        ///     Event raised when the progress bar is not longer needed
        /// </summary>
        public event EndProgressHandler EndProgress;
        /// <summary>
        ///     Event raised when a progress bar is needed
        /// </summary>
        public event InitProgressHandler InitProgress;
        /// <summary>
        ///     Event raised to report status updates
        /// </summary>
        public event UpdateStatusHandler UpdateStatus;
        /// <summary>
        ///     Event raised to report a non-fatal error
        /// </summary>
        public event ErrorMessageHandler ErrorMessage;
        /// <summary>
        ///     Event raised to report a fatal error that stops the dumping operation and should call user's attention
        /// </summary>
        public event ErrorMessageHandler StoppingErrorMessage;
        /// <summary>
        ///     Event raised to update the values of a determinate progress bar
        /// </summary>
        public event UpdateProgressHandler UpdateProgress;
        /// <summary>
        ///     Event raised to update the status of an undeterminate progress bar
        /// </summary>
        public event PulseProgressHandler PulseProgress;
        /// <summary>
        ///     Event raised when the progress bar is not longer needed
        /// </summary>
        public event EndProgressHandler2 EndProgress2;
        /// <summary>
        ///     Event raised when a progress bar is needed
        /// </summary>
        public event InitProgressHandler2 InitProgress2;
        /// <summary>
        ///     Event raised to update the values of a determinate progress bar
        /// </summary>
        public event UpdateProgressHandler2 UpdateProgress2;
    }
}