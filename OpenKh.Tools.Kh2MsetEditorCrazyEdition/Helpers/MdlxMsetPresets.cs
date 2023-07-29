using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace OpenKh.Tools.Kh2MsetEditorCrazyEdition.Helpers
{
    [XmlRoot("Presets")]
    public class MdlxMsetPresets
    {
        [XmlElement("Preset")] public MdlxMsetPreset[]? Preset { get; set; }

        public IEnumerable<MdlxMsetPreset> GetPresets() => Preset ?? Enumerable.Empty<MdlxMsetPreset>();
    }
}
