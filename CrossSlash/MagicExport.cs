// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Ficedula.FF7.Exporters;
using System.Linq;
using System.Windows;

namespace CrossSlash {
    public class MagicExport : ExportKind {
        private readonly BattleModelOptions _options = new() { Scale = 0.1f };

        public override string Help =>
@"Usage: CrossSlash MagicModel [SourceLGPOrFolder] [OutputFile] [SummonCode] [options]
    Valid options:
        /ConvertSRGBToLinear - convert colours to linear RGB
        /SwapWinding - reverse triangle winding order
        /BakeVertexColours - export vertex colours as a texture
        /Scale:value - scale model up/down from default size (0.1)
    For example:
        CrossSlash MagicModel C:\games\FF7\data\battle\magic.lgp C:\temp\shiva.glb cyvadat";

        public override object Config => _options;
        public override bool HasGui => true;
        public override string Name => "Magic Model Export";

        public override void Execute(DataSource source, string dest, IEnumerable<string> parameters) {
            var exporter = new BattleModel(source, _options);
            exporter.BuildSceneFromSummon(parameters.First()).SaveGLB(dest);
        }

        public override Window ExecuteGui(DataSource source) => new BattleExportWindow(source, magicModels: true);
    }
}
