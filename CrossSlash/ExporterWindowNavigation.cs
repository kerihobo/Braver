// This program and the accompanying materials are made available under the terms of the
// Eclipse Public License v2.0 which accompanies this distribution, and is available at
// https://www.eclipse.org/legal/epl-v20.html
//
// SPDX-License-Identifier: EPL-2.0

using Terminal.Gui;

namespace CrossSlash {
    internal static class ExporterWindowNavigation {
        public static Button CreateBackButton(Pos y) {
            var button = new Button {
                Text = "Back to main menu",
                Y = y,
                Width = Dim.Percent(35),
            };
            button.Clicked += () => Application.RequestStop();
            return button;
        }
    }
}
