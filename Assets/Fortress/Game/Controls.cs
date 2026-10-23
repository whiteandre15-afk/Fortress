using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Game
{
    /// Every button the game listens to, from keyboard and mouse or a controller, whichever is
    /// being used. Scripts ask this instead of reading devices themselves.
    public static class Controls
    {
        /// Right-stick turning speed at full tilt, in degrees per second, before look speed.
        const float StickTurn = 220;
        /// Mouse turning, in degrees per pixel moved, before look speed.
        const float MouseTurn = 0.08f;

        static Keyboard Keys => Keyboard.current;
        static Mouse Pointer => Mouse.current;
        static Gamepad Pad => Gamepad.current;
        static float rumbleUntil;
        static bool padLast;

        /// Whether the controller was the last thing touched, for showing the right button hints.
        public static bool UsingGamepad
        {
            get
            {
                if (Pad != null && Pad.wasUpdatedThisFrame && PadActive()) padLast = true;
                else if ((Keys != null && Keys.anyKey.wasPressedThisFrame) || (Pointer != null && Pointer.delta.ReadValue().sqrMagnitude > 4))
                    padLast = false;
                return padLast && Pad != null;
            }
        }

        static bool PadActive() =>
            Pad.leftStick.ReadValue().sqrMagnitude > 0.04f || Pad.rightStick.ReadValue().sqrMagnitude > 0.04f
            || (Pad.buttonSouth.isPressed || Pad.buttonEast.isPressed || Pad.buttonWest.isPressed
                || Pad.buttonNorth.isPressed || Pad.rightTrigger.isPressed || Pad.leftTrigger.isPressed
                || Pad.leftShoulder.isPressed || Pad.rightShoulder.isPressed || Pad.startButton.isPressed);

        /// Walking direction: x strafe, y forward, at most length 1.
        public static Vector2 Move
        {
            get
            {
                var move = Vector2.zero;
                if (Keys != null)
                {
                    if (Keys.wKey.isPressed) move.y += 1;
                    if (Keys.sKey.isPressed) move.y -= 1;
                    if (Keys.dKey.isPressed) move.x += 1;
                    if (Keys.aKey.isPressed) move.x -= 1;
                }
                if (Pad != null) move += Pad.leftStick.ReadValue();
                return Vector2.ClampMagnitude(move, 1);
            }
        }

        /// How far to turn this frame, in degrees: x round, y up.
        public static Vector2 Look(float dt)
        {
            var turn = Pointer != null ? Pointer.delta.ReadValue() * MouseTurn : Vector2.zero;
            if (Pad != null)
            {
                var stick = Pad.rightStick.ReadValue();
                // Squared, keeping direction: small tilts turn slowly for fine aim, full tilt turns fast.
                turn += stick * stick.magnitude * StickTurn * dt;
            }
            return turn;
        }

        /// Sprint: Shift held, or the left stick clicked (it stays on until you stop pushing forward).
        public static bool Sprint
        {
            get
            {
                if (Pad != null)
                {
                    if (Pad.leftStickButton.wasPressedThisFrame) padSprint = true;
                    if (Pad.leftStick.ReadValue().y < 0.3f) padSprint = false;
                }
                return (Keys != null && Keys.leftShiftKey.isPressed) || padSprint;
            }
        }
        static bool padSprint;

        static bool Pressed(Key key, ButtonFor button) =>
            (Keys != null && Keys[key].wasPressedThisFrame) || (Pad != null && button(Pad).wasPressedThisFrame);

        delegate UnityEngine.InputSystem.Controls.ButtonControl ButtonFor(Gamepad pad);

        public static bool Jump => Pressed(Key.Space, pad => pad.buttonSouth);
        /// Crouch: C, or clicking the right stick (B switches build mode).
        public static bool Crouch => Pressed(Key.C, pad => pad.rightStickButton);
        /// Into or out of build mode: Q or B.
        public static bool ToggleBuild => Pressed(Key.Q, pad => pad.buttonEast);
        public static bool CycleMaterial => Pressed(Key.R, pad => pad.buttonWest);
        public static bool SwitchView => Pressed(Key.V, pad => pad.buttonNorth);
        public static bool Pause => Pressed(Key.Escape, pad => pad.startButton);
        public static bool ShowFps => Keys != null && Keys.f3Key.wasPressedThisFrame;

        /// The four build buttons. On a controller each one picks its piece and places it at once:
        /// RT wall, LT floor, RB ramp, LB door or window. On the keyboard 1-4 pick and a click places.
        public enum BuildSlot { Wall, Floor, Ramp, Opening }

        static UnityEngine.InputSystem.Controls.ButtonControl PadButton(BuildSlot slot) => slot switch
        {
            BuildSlot.Wall => Pad.rightTrigger,
            BuildSlot.Floor => Pad.leftTrigger,
            BuildSlot.Ramp => Pad.rightShoulder,
            _ => Pad.leftShoulder,
        };

        /// The controller build button pressed this frame, if any.
        public static BuildSlot? PadBuildPressed
        {
            get
            {
                if (Pad == null) return null;
                foreach (BuildSlot slot in System.Enum.GetValues(typeof(BuildSlot)))
                    if (PadButton(slot).wasPressedThisFrame) return slot;
                return null;
            }
        }

        /// Whether the controller button for `slot` is held down (to keep placing while sweeping).
        public static bool PadBuildHeld(BuildSlot slot) => Pad != null && PadButton(slot).isPressed;

        /// The build piece picked by number key this frame (1-4), if any.
        public static BuildSlot? KeyBuildPick
        {
            get
            {
                if (Keys == null) return null;
                var digits = new[] { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4 };
                for (int i = 0; i < digits.Length; i++)
                    if (Keys[digits[i]].wasPressedThisFrame) return (BuildSlot)i;
                return null;
            }
        }

        /// Swap the opening between door and window: D-pad down.
        public static bool SwapOpening => Pad != null && Pad.dpad.down.wasPressedThisFrame;

        /// Mouse clicks only, for placing in build mode (where the triggers are build buttons).
        public static bool ClickPressed => Pointer != null && Pointer.leftButton.wasPressedThisFrame;
        public static bool ClickHeld => Pointer != null && Pointer.leftButton.isPressed;

        /// Use the tool: left click or right trigger.
        public static bool UsePressed =>
            (Pointer != null && Pointer.leftButton.wasPressedThisFrame) || (Pad != null && Pad.rightTrigger.wasPressedThisFrame);
        public static bool UseHeld =>
            (Pointer != null && Pointer.leftButton.isPressed) || (Pad != null && Pad.rightTrigger.isPressed);

        /// Zoom in over the shoulder: right click, or left trigger outside build mode (where it builds floors).
        public static bool Zoom(bool building) =>
            (Pointer != null && Pointer.rightButton.isPressed) || (!building && Pad != null && Pad.leftTrigger.isPressed);

        /// Next (+1) or previous (-1) tool: scroll wheel or the bumpers. 0 if neither.
        public static int ToolStep
        {
            get
            {
                if (Pad != null && Pad.rightShoulder.wasPressedThisFrame) return 1;
                if (Pad != null && Pad.leftShoulder.wasPressedThisFrame) return -1;
                float scroll = Pointer != null ? Pointer.scroll.ReadValue().y : 0;
                return Mathf.Abs(scroll) > 0.01f ? (scroll < 0 ? 1 : -1) : 0;
            }
        }

        /// The weapon picked by number key this frame (0-based), or -1.
        public static int ToolNumber
        {
            get
            {
                if (Keys == null) return -1;
                var digits = new[] { Key.Digit1, Key.Digit2, Key.Digit3 };
                for (int i = 0; i < digits.Length; i++)
                    if (Keys[digits[i]].wasPressedThisFrame) return i;
                return -1;
            }
        }

        /// In the pause menu: d-pad left/right (-1/+1), resume (A/B), quit (Select).
        public static int MenuAdjust => Pad == null ? 0 : Pad.dpad.right.wasPressedThisFrame ? 1 : Pad.dpad.left.wasPressedThisFrame ? -1 : 0;
        public static bool MenuResume => Pad != null && (Pad.buttonSouth.wasPressedThisFrame || Pad.buttonEast.wasPressedThisFrame);
        public static bool MenuQuit => Pad != null && Pad.selectButton.wasPressedThisFrame;

        /// A buzz on the controller, if there is one: `strength` 0-1 for `seconds`.
        public static void Rumble(float strength, float seconds)
        {
            if (Pad == null) return;
            Pad.SetMotorSpeeds(strength * 0.6f, strength);
            rumbleUntil = Mathf.Max(rumbleUntil, Time.unscaledTime + seconds);
        }

        /// Stops the buzz once it's run its time. Called every frame by the HUD.
        public static void Tick()
        {
            if (Pad != null && rumbleUntil > 0 && Time.unscaledTime >= rumbleUntil)
            {
                Pad.SetMotorSpeeds(0, 0);
                rumbleUntil = 0;
            }
        }
    }
}
