using System.Collections;
using Fortress.Building;
using Fortress.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fortress.Tests
{
    /// Plays the game with a virtual controller plugged in.
    public class ControllerTests
    {
        Gamepad pad;

        [TearDown]
        public void Unplug()
        {
            if (pad != null) InputSystem.RemoveDevice(pad);
            Time.captureFramerate = 0;
        }

        /// Holds the controller in `state` for `frames` frames.
        IEnumerator Hold(GamepadState state, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                InputSystem.QueueStateEvent(pad, state);
                yield return null;
            }
        }

        IEnumerator Tap(GamepadButton button)
        {
            yield return Hold(new GamepadState().WithButton(button), 2);
            yield return Hold(new GamepadState(), 2);
        }

        [UnityTest]
        public IEnumerator PlaysWithAController()
        {
            Time.captureFramerate = 60;
            Game.Game.Demo = false;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++) yield return null;
            pad = InputSystem.AddDevice<Gamepad>();
            var game = Object.FindFirstObjectByType<Game.Game>();
            var player = game.Player;
            for (int i = 0; i < 20; i++) yield return null;

            // Left stick forward walks the player forward.
            var start = player.transform.position;
            yield return Hold(new GamepadState { leftStick = Vector2.up }, 60);
            yield return Hold(new GamepadState(), 20);
            var walked = player.transform.position - start;
            Debug.Log($"CONTROLLER walked {new Vector2(walked.x, walked.z).magnitude:0.0}m");
            Assert.Greater(new Vector2(walked.x, walked.z).magnitude, 2, "left stick should walk");

            // A jumps.
            float ground = player.transform.position.y, highest = ground;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                highest = Mathf.Max(highest, player.transform.position.y);
                if (i == 2) InputSystem.QueueStateEvent(pad, new GamepadState());
            }
            Debug.Log($"CONTROLLER jumped {highest - ground:0.00}m");
            Assert.Greater(highest - ground, 0.5f, "A should jump");
            for (int i = 0; i < 40; i++) yield return null;

            // RB steps to the next tool.
            var before = game.Tools.Current;
            yield return Tap(GamepadButton.RightShoulder);
            Assert.AreNotEqual(before, game.Tools.Current, "RB should change tool");

            // B goes into build mode. RT picks the wall (ghost only), RT again puts a frame down.
            var build = game.Build;
            yield return Tap(GamepadButton.East);
            Assert.IsTrue(game.Tools.Building, "B should switch to build mode");
            int pieces = build.Kinds.Count;
            yield return Hold(new GamepadState { rightTrigger = 1 }, 12);
            yield return Hold(new GamepadState(), 10);
            Assert.AreEqual(Tool.Wall, game.Tools.Current, "RT should pick the wall");
            Assert.AreEqual(pieces, build.Kinds.Count, "the first press only shows the ghost");
            yield return Hold(new GamepadState { rightTrigger = 1 }, 30);
            yield return Hold(new GamepadState(), 10);
            Assert.AreEqual(pieces + 1, build.Kinds.Count, "pressing RT again (even held) places exactly one frame");
            Slot frame = default;
            foreach (var slot in build.Kinds.Keys) if (build.IsFrame(slot)) frame = slot;
            Assert.IsTrue(build.IsFrame(frame), "it goes down as a frame");

            // B back to combat hands over the hammer; holding RT on the frame hammers it up.
            yield return Tap(GamepadButton.East);
            Assert.IsFalse(game.Tools.Building, "B again should go back to combat");
            Assert.AreEqual(Tool.Hammer, game.Tools.Current, "after placing frames, combat starts with the hammer");
            // Look down at the frame the way a player would (the crosshair rides above the head).
            player.Face(player.transform.eulerAngles.y, -20);
            for (int i = 0; i < 5; i++) yield return null;
            float clock = 0;
            while (build.IsFrame(frame) && clock < 12)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1 });
                clock += Time.deltaTime;
                yield return null;
            }
            yield return Hold(new GamepadState(), 10);
            Debug.Log($"CONTROLLER hammered a wall up in {clock:0.0}s");
            Assert.IsFalse(build.IsFrame(frame), "holding RT with the hammer should finish the frame");
            Assert.IsTrue(build.Kinds.ContainsKey(frame), "and it's still standing");
            Assert.Greater(clock, 6, "building a piece part by part should take a good few seconds");

            // One more swing on the finished wall doesn't knock it down.
            yield return Hold(new GamepadState { rightTrigger = 1 }, 3);
            yield return Hold(new GamepadState(), 40);
            Assert.IsTrue(build.Kinds.ContainsKey(frame), "one hammer blow shouldn't wreck a finished wall");

            // RT with the hammer at a tree mines wood.
            while (game.Tools.Current != Tool.Hammer) yield return Tap(GamepadButton.RightShoulder);
            var tree = NearestTree(player.transform.position);
            // The crosshair looks past the player's right shoulder, so stand with the tree over that shoulder.
            float yaw = 0;
            var forward = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0, Mathf.Cos(yaw * Mathf.Deg2Rad));
            var right = new Vector3(forward.z, 0, -forward.x);
            var spot = tree.position - forward * 2f - right * (player.ThirdPerson ? 0.95f : 0);
            var body = player.GetComponent<CharacterController>();
            body.enabled = false;
            var terrain = Object.FindFirstObjectByType<Terrain>();
            player.transform.position = new Vector3(spot.x, terrain.SampleHeight(spot) + 0.1f, spot.z);
            body.enabled = true;
            player.Face(yaw, 0);
            for (int i = 0; i < 20; i++) yield return null;
            int wood = Stock.Count(BuildMaterial.Wood);
            yield return Hold(new GamepadState { rightTrigger = 1 }, 3);
            yield return Hold(new GamepadState(), 40);
            Debug.Log($"CONTROLLER wood {wood} -> {Stock.Count(BuildMaterial.Wood)}");
            Assert.Greater(Stock.Count(BuildMaterial.Wood), wood, "RT with the hammer at a tree should mine wood");
        }

        static Transform NearestTree(Vector3 from)
        {
            Transform best = null;
            foreach (var source in Object.FindObjectsByType<Source>(FindObjectsSortMode.None))
            {
                if (source.Material != BuildMaterial.Wood) continue;
                if (!best || (source.transform.position - from).sqrMagnitude < (best.position - from).sqrMagnitude) best = source.transform;
            }
            return best;
        }
    }
}
