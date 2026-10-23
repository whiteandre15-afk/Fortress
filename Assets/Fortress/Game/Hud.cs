using System.Collections.Generic;
using System.Linq;
using Fortress.Building;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Fortress.Game
{
    /// Crosshair, the weapon bar, the build legend, the materials readout, and the pause menu.
    public class Hud : MonoBehaviour
    {
        Player player;
        Tools tools;
        Font font;
        /// The small weapon bar in the bottom-right corner.
        RectTransform weaponBar;
        readonly List<(Tool tool, Image back, Text label)> slots = new();
        List<Tool> shownTools;
        /// The build legend along the bottom, shown in build mode: which button places which piece.
        RectTransform legend;
        readonly List<(Controls.BuildSlot slot, Image back, Text label)> legendSlots = new();
        Text mats, material, fps, modeHint;
        /// Under the crosshair: how far the frame the hammer is on has been built.
        RectTransform buildBar, buildFill;
        /// Set by a controller "resume" press; the game unpauses next frame so that same press
        /// doesn't also make the player jump.
        bool resumeNext;
        float fpsClock;
        int fpsFrames;

        public void Setup(Player player, Tools tools)
        {
            this.player = player;
            this.tools = tools;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            player.LookSpeed = PlayerPrefs.GetFloat("lookSpeed", 1);

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var crosshair = Panel(transform, new Color(1, 1, 1, 0.85f));
            Place(crosshair, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));

            buildBar = Panel(transform, new Color(0, 0, 0, 0.55f));
            Place(buildBar, new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(160, 12));
            buildFill = Panel(buildBar, new Color(0.95f, 0.8f, 0.35f, 0.95f));
            buildFill.anchorMin = new Vector2(0, 0);
            buildFill.anchorMax = new Vector2(0, 1);
            buildFill.pivot = new Vector2(0, 0.5f);
            buildFill.offsetMin = buildFill.offsetMax = Vector2.zero;

            weaponBar = new GameObject("Weapons", typeof(RectTransform)).GetComponent<RectTransform>();
            weaponBar.SetParent(transform, false);
            Place(weaponBar, new Vector2(1, 0), new Vector2(-30, 30), Vector2.zero);

            legend = new GameObject("Build legend", typeof(RectTransform)).GetComponent<RectTransform>();
            legend.SetParent(transform, false);
            Place(legend, new Vector2(0.5f, 0), new Vector2(0, 30), Vector2.zero);
            const float slotWidth = 120, slotHeight = 70, gap = 10;
            var slotsInOrder = new[] { Controls.BuildSlot.Wall, Controls.BuildSlot.Floor, Controls.BuildSlot.Ramp, Controls.BuildSlot.Opening };
            float legendWidth = slotsInOrder.Length * slotWidth + (slotsInOrder.Length - 1) * gap;
            for (int i = 0; i < slotsInOrder.Length; i++)
            {
                var back = Panel(legend, Color.black);
                Place(back, new Vector2(0.5f, 0), new Vector2(-legendWidth / 2 + slotWidth / 2 + i * (slotWidth + gap), slotHeight / 2), new Vector2(slotWidth, slotHeight));
                legendSlots.Add((slotsInOrder[i], back.GetComponent<Image>(), Label(back, 20, TextAnchor.MiddleCenter)));
            }

            // A one-line reminder of how to switch modes, above whichever bar is showing.
            var hintBack = Panel(transform, new Color(0, 0, 0, 0.45f));
            Place(hintBack, new Vector2(0.5f, 0), new Vector2(0, 30 + 70 + 24), new Vector2(860, 32));
            modeHint = Label(hintBack, 19, TextAnchor.MiddleCenter);

            var matsBack = Panel(transform, new Color(0, 0, 0, 0.45f));
            Place(matsBack, new Vector2(1, 1), new Vector2(-30, -30), new Vector2(330, 48), pivot: new Vector2(1, 1));
            mats = Label(matsBack, 24, TextAnchor.MiddleCenter);

            var materialBack = Panel(transform, new Color(0, 0, 0, 0.5f));
            Place(materialBack, new Vector2(0.5f, 0), new Vector2(0, 30 + 70 + 60), new Vector2(240, 40));
            material = Label(materialBack, 21, TextAnchor.MiddleCenter);

            var fpsBack = Panel(transform, new Color(0, 0, 0, 0.45f));
            Place(fpsBack, new Vector2(0, 1), new Vector2(30, -30), new Vector2(150, 40), pivot: new Vector2(0, 1));
            fps = Label(fpsBack, 20, TextAnchor.MiddleCenter);
            fpsBack.gameObject.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            if (!player) return;
            Controls.Tick();
            if (resumeNext)
            {
                resumeNext = false;
                SetPaused(false);
            }
            else if (Controls.Pause)
            {
                SetPaused(!player.Paused);
            }
            else if (player.Paused)
            {
                // The pause menu on a controller: d-pad changes look speed, A or B resumes, Select quits.
                int adjust = Controls.MenuAdjust;
                if (adjust != 0) SetLookSpeed(Mathf.Clamp(Mathf.Round((player.LookSpeed + adjust * 0.1f) * 10) / 10, 0.3f, 2f));
                if (Controls.MenuResume) resumeNext = true;
                if (Controls.MenuQuit) Application.Quit();
            }
            // F3 shows frames per second, refreshed twice a second.
            if (Controls.ShowFps) fps.transform.parent.gameObject.SetActive(!fps.transform.parent.gameObject.activeSelf);
            fpsFrames++;
            fpsClock += Time.unscaledDeltaTime;
            if (fpsClock >= 0.5f)
            {
                fps.text = $"{fpsFrames / fpsClock:0} fps";
                fpsFrames = 0;
                fpsClock = 0;
            }
            // Clicking back into the window resumes, the way a browser game re-grabs the mouse.
            if (player.Paused == false && Cursor.lockState != CursorLockMode.Locked && Mouse.current?.leftButton.wasPressedThisFrame == true)
                Cursor.lockState = CursorLockMode.Locked;

            bool pad = Controls.UsingGamepad;
            bool building = tools.Building;

            // Weapons: a small bar in the corner, only outside build mode.
            var available = tools.Available;
            if (shownTools == null || !available.SequenceEqual(shownTools)) BuildWeaponBar(available);
            weaponBar.gameObject.SetActive(!building);
            for (int i = 0; i < slots.Count; i++)
            {
                var (tool, back, label) = slots[i];
                bool selected = tool == tools.Current;
                back.color = selected ? new Color(1, 1, 1, 0.92f) : new Color(0, 0, 0, 0.45f);
                label.color = selected ? Color.black : Color.white;
                label.text = pad ? Name(tool) : $"{i + 1}  {Name(tool)}";
            }

            // Build legend: each button and the piece it places, the one in hand lit up.
            legend.gameObject.SetActive(building);
            foreach (var (slot, back, label) in legendSlots)
            {
                bool selected = building && tools.Armed && SlotOf(tools.Current) == slot;
                back.color = selected ? new Color(1, 1, 1, 0.92f) : new Color(0, 0, 0, 0.5f);
                label.color = selected ? Color.black : Color.white;
                string piece = slot == Controls.BuildSlot.Opening ? (tools.OpeningIsWindow ? "Window" : "Door") : slot.ToString();
                string button = pad ? slot switch
                {
                    Controls.BuildSlot.Wall => "RT",
                    Controls.BuildSlot.Floor => "LT",
                    Controls.BuildSlot.Ramp => "RB",
                    _ => "LB",
                } : ((int)slot + 1).ToString();
                label.text = $"{button}\n{piece}";
            }
            float built = tools.AimedFrameProgress;
            buildBar.gameObject.SetActive(built >= 0);
            if (built >= 0) buildFill.sizeDelta = new Vector2(160 * built, 0);

            modeHint.text = building
                ? (!tools.Armed
                    ? (pad ? "pick a piece:  RT wall · LT floor · RB ramp · LB door        B  back" : "pick a piece:  1 wall · 2 floor · 3 ramp · 4 door        Q  back")
                    : (pad ? "same button again  place frame     ▼ door/window     B  back to hammer" : "click  place frame     4 again  door/window     Q  back to hammer"))
                : (pad ? "B  build mode     LB / RB  weapons" : "Q  build mode     1-3 / scroll  weapons");

            string Count(BuildMaterial m) => Stock.Unlimited ? "∞" : Stock.Count(m).ToString();
            mats.text = $"Wood {Count(BuildMaterial.Wood)}    Brick {Count(BuildMaterial.Brick)}    Metal {Count(BuildMaterial.Metal)}";
            material.transform.parent.gameObject.SetActive(building);
            material.text = $"[{(pad ? "X" : "R")}]  {tools.Material}";
        }

        static string Name(Tool tool) => tool == Tool.Hands ? "Fists" : tool.ToString();

        static Controls.BuildSlot SlotOf(Tool piece) => piece switch
        {
            Tool.Wall => Controls.BuildSlot.Wall,
            Tool.Floor => Controls.BuildSlot.Floor,
            Tool.Ramp => Controls.BuildSlot.Ramp,
            _ => Controls.BuildSlot.Opening,
        };

        /// The weapon bar: small boxes stacked from the bottom-right corner leftward.
        void BuildWeaponBar(List<Tool> available)
        {
            shownTools = available;
            foreach (Transform child in weaponBar) Destroy(child.gameObject);
            slots.Clear();
            const float width = 120, height = 46, gap = 8;
            for (int i = 0; i < available.Count; i++)
            {
                var back = Panel(weaponBar, Color.black);
                int fromRight = available.Count - 1 - i;
                Place(back, new Vector2(1, 0), new Vector2(-fromRight * (width + gap), 0), new Vector2(width, height), pivot: new Vector2(1, 0));
                slots.Add((available[i], back.GetComponent<Image>(), Label(back, 19, TextAnchor.MiddleCenter)));
            }
        }

        void SetLookSpeed(float speed)
        {
            player.LookSpeed = speed;
            PlayerPrefs.SetFloat("lookSpeed", speed);
        }

        void SetPaused(bool paused)
        {
            player.Paused = paused;
            Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused;
        }

        /// The Esc menu: drawn with immediate-mode GUI, which needs no event system or prefabs.
        void OnGUI()
        {
            if (!player || !player.Paused) return;
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float width = 920, height = 440, x = (Screen.width / scale - width) / 2, y = (1080 - height) / 2;
            bool pad = Controls.UsingGamepad;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 38, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            var text = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            var button = new GUIStyle(GUI.skin.button) { fontSize = 24 };
            GUI.Label(new Rect(x, y + 20, width, 50), "Paused", title);
            GUI.Label(new Rect(x + 40, y + 95, width - 80, 32), $"Look speed  {player.LookSpeed:0.0}×" + (pad ? "      (D-pad ◀ ▶)" : ""), text);
            float speed = GUI.HorizontalSlider(new Rect(x + 40, y + 140, width - 80, 30), player.LookSpeed, 0.3f, 2f);
            if (!Mathf.Approximately(speed, player.LookSpeed)) SetLookSpeed(speed);
            var hints = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            GUI.Label(new Rect(x + 40, y + 180, width - 80, 150), pad
                ? "Move L-stick · Look R-stick · Sprint click L-stick · Jump A · Crouch click R-stick · Camera Y\nCombat: use RT · zoom LT · weapons LB/RB · B build\nBuild: RT wall · LT floor · RB ramp · LB door · X material · B combat"
                : "Move WASD · Sprint Shift · Jump Space · Crouch C · Camera V\nCombat: use click · zoom right click · weapons 1-3 / scroll · Q build\nBuild: 1 wall · 2 floor · 3 ramp · 4 door/window · click place · R material · Q combat", hints);
            if (GUI.Button(new Rect(x + 40, y + 350, 240, 60), pad ? "Resume (A)" : "Resume", button)) SetPaused(false);
            if (GUI.Button(new Rect(x + width - 280, y + 350, 240, 60), pad ? "Quit (Select)" : "Quit", button)) Application.Quit();
        }

        static RectTransform Panel(Transform parent, Color color)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel.GetComponent<RectTransform>();
        }

        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        Text Label(RectTransform parent, int size, TextAnchor alignment)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }
    }
}
