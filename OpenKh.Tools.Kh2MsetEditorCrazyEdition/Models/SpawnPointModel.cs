using OpenKh.Kh2.Ard;
using OpenKh.Tools.Kh2MsetEditorCrazyEdition.Interfaces;
using System.Collections.Generic;

namespace OpenKh.Tools.Kh2MsetEditorCrazyEdition.Models
{
    class SpawnPointModel
    {
        public SpawnPointModel(IObjEntryController objEntryCtrl, string name, List<SpawnPoint> spawnPoints)
        {
            ObjEntryCtrl = objEntryCtrl;
            Name = name;
            SpawnPoints = spawnPoints;
        }

        public IObjEntryController ObjEntryCtrl { get; }
        public string Name { get; set; }
        public List<SpawnPoint> SpawnPoints { get; }
    }
}
