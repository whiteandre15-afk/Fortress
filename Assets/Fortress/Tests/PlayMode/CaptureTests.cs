using System.Collections;
using System.IO;
using Fortress.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fortress.Tests
{
    /// Boots the game with the demo house and saves what the player sees with each tool in hand,
    /// to Captures/ in the project folder, for checking by eye.
    public class CaptureTests
    {
        [UnityTest]
        public IEnumerator CaptureEachTool()
        {
            Game.Game.Demo = true;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++) yield return null;
            var game = Object.FindFirstObjectByType<Game.Game>();
            Assert.IsNotNull(game, "the scene should hold the game");

            // The editor compiles shaders in the background and draws a placeholder until they're
            // ready; render a few frames and let it catch up before taking pictures.
            for (int i = 0; i < 5; i++)
            {
                Render(game.Player.View);
                yield return new WaitForSecondsRealtime(1);
            }

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures"));
            Directory.CreateDirectory(folder);
            foreach (var tool in new[] { Tool.Hands, Tool.Hammer, Tool.Gun, Tool.Wall })
            {
                if (tool.IsPiece()) game.Tools.Pick(tool);
                else game.Tools.Select(tool);
                for (int i = 0; i < 40; i++) yield return null;
                File.WriteAllBytes(Path.Combine(folder, $"{tool}.png"), Render(game.Player.View));
            }

            // A frame just put down, then walls of each material part-way built, up on the demo
            // house's roof where nothing's buried in the hill.
            var roof = default(Fortress.Building.Slot);
            foreach (var slot in game.Build.Kinds.Keys)
                if (slot.Layer == Fortress.Building.Layer.Floor && slot.Level > roof.Level) roof = slot;
            var wallSlot = new Fortress.Building.Slot(roof.X, roof.Z + 1, roof.Level, Fortress.Building.Layer.EdgeX);
            var wallCentre = new Vector3((wallSlot.X + 0.5f) * Fortress.Building.BuildGrid.Unit, wallSlot.Level * Fortress.Building.BuildGrid.Storey + 1.6f, wallSlot.Z * Fortress.Building.BuildGrid.Unit);
            foreach (var (material, share) in new[] { (Fortress.Building.BuildMaterial.Brick, 0.4f), (Fortress.Building.BuildMaterial.Wood, 0.5f), (Fortress.Building.BuildMaterial.Metal, 0.5f) })
            {
                game.Build.Remove(wallSlot, falling: false);
                Assert.IsTrue(game.Build.Place(Fortress.Building.PieceKind.Wall, wallSlot, material, 0), "wall frame on the roof");
                game.Tools.Select(Tool.Hammer);
                // Stand on the ground in front of it, looking up at it.
                var spot = wallCentre + new Vector3(1.2f, 0, 7);
                var terrain = Object.FindFirstObjectByType<Terrain>();
                float ground = terrain.SampleHeight(spot);
                var look = wallCentre - new Vector3(spot.x, ground + 2.3f, spot.z);
                Stand(game.Player, spot.x, spot.z, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg,
                      Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg);
                if (material == Fortress.Building.BuildMaterial.Brick)
                {
                    for (int i = 0; i < 20; i++) yield return null;
                    File.WriteAllBytes(Path.Combine(folder, "Frame.png"), Render(game.Player.View));
                }
                game.Build.Hammer(wallSlot, share);
                yield return new WaitForSeconds(1.5f);
                File.WriteAllBytes(Path.Combine(folder, $"Building {material}.png"), Render(game.Player.View));
            }
            game.Build.Remove(wallSlot, falling: false);

            // Views of the world itself, with empty hands.
            game.Tools.Select(Tool.Hands);
            var forest = DensestForest();
            foreach (var (name, x, z, yaw, tilt) in new[]
            {
                ("Landscape", 8f, 8f, 150f, -2f),
                ("Brick close-up", -1.5f, 1.5f, 180f, -8f),
                ("Forest", forest.x, forest.y, 30f, 4f),
            })
            {
                Stand(game.Player, x, z, yaw, tilt);
                for (int i = 0; i < 40; i++) yield return null;
                File.WriteAllBytes(Path.Combine(folder, $"{name}.png"), Render(game.Player.View));
            }

            // Close views of each kind of rock, and the grass at knee height.
            foreach (var kind in new[] { "MossRocks", "Boulder" })
            {
                var rock = NearestNamed(kind);
                if (!rock) continue;
                var spot = rock.position + Quaternion.Euler(0, 35, 0) * Vector3.back * 7;
                var look = rock.position - spot;
                Stand(game.Player, spot.x, spot.z, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, -6);
                for (int i = 0; i < 40; i++) yield return null;
                File.WriteAllBytes(Path.Combine(folder, $"Rock {kind}.png"), Render(game.Player.View));
            }
            Stand(game.Player, 30, 30, 200, -18);
            game.Player.Crouching = true;
            for (int i = 0; i < 60; i++) yield return null;
            File.WriteAllBytes(Path.Combine(folder, "Grass low.png"), Render(game.Player.View));
            game.Player.Crouching = false;

            // A rough cost: how long one full frame takes to draw at 1600x900, averaged over ten.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Render(game.Player.View);
            Debug.Log($"CAPTURE render: {clock.ElapsedMilliseconds / 10f:0.0} ms per frame in the forest view (includes reading back the picture)");
            Game.Game.Demo = false;
        }

        /// Moves the player to stand on the ground at (x, z), looking `yaw` degrees round and `tilt` up.
        static void Stand(Player player, float x, float z, float yaw, float tilt)
        {
            var body = player.GetComponent<CharacterController>();
            body.enabled = false;
            var terrain = Object.FindFirstObjectByType<Terrain>();
            player.transform.position = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0, z)) + 0.1f, z);
            body.enabled = true;
            player.Face(yaw, tilt);
        }

        /// The rock nearest the start whose name starts with `kind`.
        static Transform NearestNamed(string kind)
        {
            Transform best = null;
            foreach (var source in Object.FindObjectsByType<Source>(FindObjectsSortMode.None))
            {
                if (!source.name.StartsWith(kind)) continue;
                if (!best || source.transform.position.sqrMagnitude < best.position.sqrMagnitude) best = source.transform;
            }
            return best;
        }

        /// The thickest woods within 300m of the start.
        static Vector2 DensestForest()
        {
            var best = Vector2.zero;
            float densest = -1;
            for (int x = -300; x <= 300; x += 15)
            for (int z = -300; z <= 300; z += 15)
            {
                float forest = WorldShape.Forest(x, z);
                if (forest > densest) (best, densest) = (new Vector2(x, z), forest);
            }
            return best;
        }

        /// Draws one frame from `view` into a picture. Batchmode never reaches end-of-frame, so the
        /// screen can't be grabbed; the HUD is moved in front of the camera so it's drawn too.
        static byte[] Render(Camera view)
        {
            var hud = Object.FindFirstObjectByType<Canvas>();
            hud.renderMode = RenderMode.ScreenSpaceCamera;
            hud.worldCamera = view;
            hud.planeDistance = 0.06f;
            var target = new RenderTexture(1600, 900, 24);
            view.targetTexture = target;
            view.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            picture.Apply();
            RenderTexture.active = null;
            view.targetTexture = null;
            hud.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.Destroy(target);
            var png = picture.EncodeToPNG();
            Object.Destroy(picture);
            return png;
        }
    }
}
