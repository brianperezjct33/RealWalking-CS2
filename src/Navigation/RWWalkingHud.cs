// SPDX-License-Identifier: MIT
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    // Lightweight walking-status label. Movement and surface matching live in Navigation/.
    internal sealed partial class RWWalkingController
    {
        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || RWWalkState.Mode == RWTravelMode.Free) return;
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.box);
                _textStyle.fontSize = 12;
                _textStyle.alignment = TextAnchor.MiddleLeft;
                _textStyle.normal.textColor = Color.white;
            }
            string label = "RW G1.9 | " + RWWalkState.Mode + " | " + RWWalkState.Surface + " | " + RWWalkState.Status;
            GUI.Box(new Rect(12f, Screen.height - 42f, Mathf.Min(Screen.width - 24f, 720f), 31f), label, _textStyle);
        }
    }
}
