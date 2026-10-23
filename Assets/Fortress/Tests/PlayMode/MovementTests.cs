using System.Collections;
using Fortress.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fortress.Tests
{
    public class MovementTests
    {
        /// Sprinting down a real hillside should keep the feet on the ground the whole way, not
        /// hop off and land again and again.
        [UnityTest]
        public IEnumerator SprintsDownhillWithoutBouncing()
        {
            Game.Game.Demo = false;
            // Batchmode runs as fast as it can; step time at a steady 60 fps like the real game.
            Time.captureFramerate = 60;
            SceneManager.LoadScene("Main");
            for (int i = 0; i < 30; i++) yield return null;
            var game = Object.FindFirstObjectByType<Game.Game>();
            var terrain = Object.FindFirstObjectByType<Terrain>();
            var (top, downhill) = SteepSlope(terrain);

            var player = game.Player;
            var body = player.GetComponent<CharacterController>();
            body.enabled = false;
            player.transform.position = top + Vector3.up * 0.1f;
            body.enabled = true;
            player.Face(Mathf.Atan2(downhill.x, downhill.y) * Mathf.Rad2Deg, 0);
            for (int i = 0; i < 30; i++) yield return null;

            int frames = 0, grounded = 0, landingsBefore = player.Landings;
            float startHeight = player.transform.position.y;
            player.TestInput = Vector2.up;
            player.TestSprint = true;
            int cameraBuried = 0;
            for (float t = 0; t < 3; t += Time.deltaTime)
            {
                frames++;
                if (player.Grounded) grounded++;
                // The spring arm should keep the camera above the hillside the whole way down.
                var eye = player.CameraPosition;
                if (eye.y < terrain.SampleHeight(eye) + 0.05f) cameraBuried++;
                yield return null;
            }
            Assert.AreEqual(0, cameraBuried, "camera went inside the hill");
            player.TestInput = null;
            Time.captureFramerate = 0;
            float share = grounded / (float)frames;
            int landings = player.Landings - landingsBefore;
            Debug.Log($"MOVEMENT downhill: grounded {share:P0} of {frames} frames, {landings} landings, dropped {startHeight - player.transform.position.y:0.0}m");
            Assert.Greater(startHeight - player.transform.position.y, 2, "the run should actually go down the hill");
            Assert.GreaterOrEqual(share, 0.95f, "feet should stay on the ground");
            Assert.LessOrEqual(landings, 1, "no bouncing");
        }

        /// The steepest walkable spot on the map, and which way is downhill.
        static (Vector3 top, Vector2 downhill) SteepSlope(Terrain terrain)
        {
            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            Vector2 best = default, downhill = default;
            float flattest = 1;
            for (int radius = 20; radius < 760; radius += 10)
            for (int step = 0; step < 36; step++)
            {
                float angle = step * 10 * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * radius, z = Mathf.Sin(angle) * radius;
                var normal = data.GetInterpolatedNormal((x - origin.x) / data.size.x, (z - origin.z) / data.size.z);
                // Steepest, but still walkable (under 45 degrees).
                if (normal.y < flattest && normal.y > 0.72f)
                {
                    flattest = normal.y;
                    best = new Vector2(x, z);
                    downhill = new Vector2(normal.x, normal.z).normalized;
                }
            }
            Debug.Log($"MOVEMENT slope: {Mathf.Acos(flattest) * Mathf.Rad2Deg:0} degrees at {best}");
            // Start a little uphill so the run crosses the steep part.
            var top = best - downhill * 8;
            return (new Vector3(top.x, terrain.SampleHeight(new Vector3(top.x, 0, top.y)), top.y), downhill);
        }
    }
}
