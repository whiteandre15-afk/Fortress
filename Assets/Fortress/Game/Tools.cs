using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fortress.Building;
using UnityEngine;

namespace Fortress.Game
{
    public enum Tool { Hands, Gun, Wall, Door, Window, Floor, Ramp, Hammer }

    public static class ToolInfo
    {
        public static bool IsPiece(this Tool tool) => tool is >= Tool.Wall and <= Tool.Ramp;

        public static PieceKind Piece(this Tool tool) => tool switch
        {
            Tool.Wall => PieceKind.Wall,
            Tool.Door => PieceKind.Door,
            Tool.Window => PieceKind.Window,
            Tool.Floor => PieceKind.Floor,
            _ => PieceKind.Ramp,
        };

        /// How often holding the mouse button repeats the action, in seconds.
        public static float RepeatDelay(this Tool tool) => tool switch
        {
            Tool.Hands => 0.35f,
            Tool.Gun => 0.15f,
            Tool.Hammer => 0.45f,
            _ => 0.18f,
        };
    }

    /// Something in the world that can be mined: a tree, rock or wreck, and how many more hits it takes.
    public class Source : MonoBehaviour
    {
        public BuildMaterial Material;
        public int Hits;
    }

    /// A loot crate and how many more hits it takes before it breaks open.
    public class Crate : MonoBehaviour
    {
        public int Hits = 3;
    }

    /// The tool in hand and what it does: punching, shooting, swinging the hammer and placing pieces.
    public class Tools : MonoBehaviour
    {
        /// Furthest a punch reaches.
        const float PunchRange = 2.2f;
        /// Furthest the hammer reaches: far enough for a frame just placed (it snaps out to a grid line
        /// up to about 3.5m away), and for mining.
        const float HammerRange = 4.5f;

        public Tool Current { get; private set; } = Tool.Hands;
        public BuildMaterial Material { get; private set; } = BuildMaterial.Wood;
        public bool HasGun { get; private set; } = Stock.Unlimited;
        /// Build mode: the controller's triggers and bumpers each place a piece. Q / B switches.
        public bool Building { get; private set; }
        /// Whether the door/window button places windows (true) or doors.
        public bool OpeningIsWindow { get; private set; }
        /// The weapon to go back to when leaving build mode, and the piece to come back to.
        Tool lastWeapon = Tool.Hands, lastPiece = Tool.Wall;

        Player player;
        BuildSystem build;
        HeldRig rig;
        AudioSource voice;
        float cooldown;
        /// Seconds since the tool was last used, so the body keeps facing the target for a moment after.
        float sinceUse = 99;
        float? swingLanding;
        /// The build button picked in build mode, showing its ghost; pressing it again places a frame.
        /// Nothing is picked straight after entering build mode.
        Controls.BuildSlot? armed;
        /// Frames were put down this visit to build mode: leaving goes straight to the hammer.
        bool placedFrames;
        /// What the hammer is aimed at, for the HUD's progress bar: the frame's slot, or null.
        public Slot? AimedFrame { get; private set; }
        /// How built the frame the hammer is aimed at is (0-1), or -1 when it isn't on a frame.
        public float AimedFrameProgress => AimedFrame is { } frame ? build.Progress(frame) : -1;
        /// Whether a piece is picked in build mode, showing its ghost.
        public bool Armed => armed != null;
        readonly List<Transform> pickups = new();

        public void Setup(Player player, BuildSystem build, HeldRig rig, AudioSource voice)
        {
            this.player = player;
            this.build = build;
            this.rig = rig;
            this.voice = voice;
        }

        /// The weapons, in order: what LB/RB and 1-3 step through outside build mode. The gun appears once picked up.
        public List<Tool> Available => new[] { Tool.Hands, Tool.Gun, Tool.Hammer }.Where(tool => tool != Tool.Gun || HasGun).ToList();

        /// Takes `tool` in hand: a weapon in combat mode, or a piece (which goes into build mode).
        public void Select(Tool tool)
        {
            if (tool == Tool.Gun && !HasGun) return;
            if (tool.IsPiece()) lastPiece = tool;
            else lastWeapon = tool;
            Building = tool.IsPiece();
            player.Building = Building;
            if (tool == Current) return;
            Current = tool;
            rig.Pose(tool);
            swingLanding = null;
        }

        /// The piece each build button places.
        Tool PieceFor(Controls.BuildSlot slot) => slot switch
        {
            Controls.BuildSlot.Wall => Tool.Wall,
            Controls.BuildSlot.Floor => Tool.Floor,
            Controls.BuildSlot.Ramp => Tool.Ramp,
            _ => OpeningIsWindow ? Tool.Window : Tool.Door,
        };

        Controls.BuildSlot SlotOf(Tool piece) => piece switch
        {
            Tool.Wall => Controls.BuildSlot.Wall,
            Tool.Floor => Controls.BuildSlot.Floor,
            Tool.Ramp => Controls.BuildSlot.Ramp,
            _ => Controls.BuildSlot.Opening,
        };

        public void Select(BuildMaterial material) => Material = material;

        void Update()
        {
            if (player.Paused) return;
            float dt = Time.deltaTime;
            cooldown = Mathf.Max(0, cooldown - dt);
            sinceUse += dt;
            if (Controls.CycleMaterial) Material = (BuildMaterial)(((int)Material + 1) % 3);
            if (Controls.ToggleBuild)
            {
                if (Building) Select(placedFrames ? Tool.Hammer : lastWeapon);
                else
                {
                    Select(lastPiece);
                    armed = null;
                    placedFrames = false;
                }
            }
            if (Building) UpdateBuilding();
            else UpdateCombat();
            // Aiming a gun or a build piece, or just after a punch or swing, the body faces the crosshair.
            player.Strafing = Current == Tool.Gun || Current.IsPiece() || sinceUse < 1;
            if (swingLanding is { } left)
            {
                swingLanding = left <= dt ? null : left - dt;
                if (swingLanding == null) HammerHit();
            }
            CollectPickups();
        }

        void UpdateCombat()
        {
            var weapons = Available;
            int number = Controls.ToolNumber;
            if (number >= 0 && number < weapons.Count) Select(weapons[number]);
            int step = Controls.ToolStep;
            if (step != 0) Select(weapons[(weapons.IndexOf(Current) + step + weapons.Count) % weapons.Count]);
            if (Controls.UsePressed || (Controls.UseHeld && cooldown == 0)) UseNow();
        }

        /// Build mode. A piece button picks its piece and shows the ghost; pressing the same button
        /// again puts a frame down there. On the keyboard 1-4 pick and a click places. Nothing repeats.
        void UpdateBuilding()
        {
            if (Controls.SwapOpening)
            {
                OpeningIsWindow = !OpeningIsWindow;
                if (armed == Controls.BuildSlot.Opening) Select(PieceFor(Controls.BuildSlot.Opening));
            }
            if (Controls.KeyBuildPick is { } key)
            {
                // Pressing 4 again swaps door and window.
                if (key == Controls.BuildSlot.Opening && armed == key) OpeningIsWindow = !OpeningIsWindow;
                Arm(key);
            }
            if (Controls.PadBuildPressed is { } pressed)
            {
                if (armed == pressed) UseNow();
                else Arm(pressed);
                return;
            }
            if (Controls.ClickPressed && armed != null) UseNow();
        }

        /// Goes into build mode with `piece` picked and its ghost showing (for tests and captures).
        public void Pick(Tool piece) => Arm(SlotOf(piece));

        void Arm(Controls.BuildSlot slot)
        {
            armed = slot;
            Select(PieceFor(slot));
        }

        void UseNow()
        {
            cooldown = Current.RepeatDelay();
            sinceUse = 0;
            Use();
        }

        void LateUpdate()
        {
            if (!player) return;
            build.ShowGhost(Building && armed != null ? Current.Piece() : null, player.Aim, player.BodyLevel, player.Facing, Stock.CanAfford(Material));
            // The hammer shows how far along the frame it's pointed at is.
            AimedFrame = null;
            if (!Building && Current == Tool.Hammer && Physics.Raycast(player.Aim, out var hit, HammerRange)
                && build.TryGetSlot(hit.collider, out var slot) && build.IsFrame(slot))
                AimedFrame = slot;
        }

        void Use()
        {
            switch (Current)
            {
                case Tool.Hands:
                    rig.Punch();
                    Play("punch");
                    player.Kick(0.7f);
                    Controls.Rumble(0.25f, 0.08f);
                    Strike(PunchRange, 10, mines: true);
                    break;
                case Tool.Gun:
                    rig.Recoil();
                    Play("shot");
                    player.Kick(2);
                    Controls.Rumble(0.6f, 0.12f);
                    Strike(250, 25, mines: false);
                    break;
                case Tool.Hammer:
                    if (rig.Swinging || swingLanding != null) return;
                    swingLanding = rig.Swing();
                    Play("punch");
                    break;
                default:
                    var slot = build.AimedSlot(Current.Piece(), player.Aim, player.BodyLevel, player.Facing);
                    if (build.Place(Current.Piece(), slot, Material, player.Facing))
                    {
                        placedFrames = true;
                        Play("place");
                        Controls.Rumble(0.15f, 0.05f);
                    }
                    break;
            }
        }

        /// A hammer blow knocks out the built piece in the crosshair, or up close, mines a tree, rock
        /// or wreck and breaks crates.
        void HammerHit()
        {
            if (!Physics.Raycast(player.Aim, out var hit, HammerRange)) return;
            player.Kick(1.4f);
            if (build.TryGetSlot(hit.collider, out var slot))
            {
                Burst(hit.point, Debris(build.MaterialAt(slot), mined: false));
                if (build.IsFrame(slot))
                {
                    // About eighteen swings build a piece, laying its next few parts each time.
                    bool done = build.Hammer(slot, 1 / 18f);
                    Play(done ? "place" : "crack");
                    Controls.Rumble(done ? 0.8f : 0.35f, done ? 0.2f : 0.08f);
                    return;
                }
                // On a finished piece the hammer is a wrecking tool: a few heavy blows take it down.
                bool broke = build.Damage(slot, 35);
                Play(broke ? "remove" : "crack");
                Controls.Rumble(broke ? 1 : 0.6f, broke ? 0.25f : 0.1f);
                return;
            }
            Controls.Rumble(0.7f, 0.12f);
            Impact(hit, 0, mines: true);
        }

        /// Hits whatever the crosshair is on, out to `range` metres.
        void Strike(float range, float damage, bool mines)
        {
            if (Physics.Raycast(player.Aim, out var hit, range)) Impact(hit, damage, mines);
        }

        /// Breaks crates, damages built pieces, and when `mines` is set, takes materials from trees,
        /// rocks and wrecks. Throws off debris for whatever was hit.
        void Impact(RaycastHit hit, float damage, bool mines)
        {
            var crate = hit.collider.GetComponentInParent<Crate>();
            if (crate)
            {
                Burst(hit.point, Debris(BuildMaterial.Wood, mined: true));
                HitCrate(crate);
            }
            else if (build.TryGetSlot(hit.collider, out var slot))
            {
                Burst(hit.point, Debris(build.MaterialAt(slot), mined: false));
                bool broke = build.Damage(slot, damage);
                if (broke) Controls.Rumble(1, 0.25f);
                Play(broke ? "remove" : "crack");
            }
            else if (mines && hit.collider.GetComponentInParent<Source>() is { } source)
            {
                Burst(hit.point, Debris(source.Material, mined: true));
                Mine(source);
            }
            else
            {
                Burst(hit.point, (new Color(0.9f, 0.9f, 0.85f), false));
            }
        }

        /// Takes one hit's worth of material from a tree, rock or wreck.
        void Mine(Source source)
        {
            Stock.Add(source.Material, source.Material.Stats().PerHit);
            Play("crack");
            source.Hits -= 1;
            if (source.Hits > 0)
            {
                StartCoroutine(Wobble(source.transform));
                return;
            }
            // Spent: it topples or crumbles away.
            foreach (var collider in source.GetComponentsInChildren<Collider>()) collider.enabled = false;
            StartCoroutine(Spend(source.transform, topple: source.Material == BuildMaterial.Wood));
            Destroy(source);
        }

        static IEnumerator Wobble(Transform node)
        {
            var home = node.position;
            for (float t = 0; t < 0.09f && node; t += Time.deltaTime)
            {
                node.position = home + Vector3.right * (0.05f * Mathf.Sin(t / 0.09f * Mathf.PI));
                yield return null;
            }
            if (node) node.position = home;
        }

        static IEnumerator Spend(Transform node, bool topple)
        {
            var start = node.rotation;
            var size = node.localScale;
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                if (topple) node.rotation = start * Quaternion.Euler(0, 0, -90 * Mathf.Min(t / 0.7f, 1));
                else node.localScale = size * Mathf.Max(0.05f, 1 - t / 0.5f);
                if (t > 0.6f) node.localScale = size * Mathf.Max(0.05f, 1 - (t - 0.6f) / 0.3f);
                yield return null;
            }
            Destroy(node.gameObject);
        }

        /// Crates take three hits, from fists or bullets, then drop their gun.
        void HitCrate(Crate crate)
        {
            Play("crack");
            crate.Hits -= 1;
            if (crate.Hits > 0)
            {
                StartCoroutine(Wobble(crate.transform));
                return;
            }
            var spot = crate.transform.position + Vector3.up * 0.6f;
            Destroy(crate.gameObject);
            var pickup = new GameObject("Rifle pickup").transform;
            pickup.position = spot;
            var gun = Instantiate(GameAssets.Model("Rifle"), pickup).transform;
            gun.localRotation = Quaternion.Euler(0, 0, 20);
            foreach (var collider in pickup.GetComponentsInChildren<Collider>()) Destroy(collider);
            pickups.Add(pickup);
        }

        void CollectPickups()
        {
            var here = new Vector2(player.transform.position.x, player.transform.position.z);
            foreach (var pickup in pickups.ToList())
            {
                pickup.Rotate(0, 90 * Time.deltaTime, 0, Space.World);
                if (Vector2.Distance(new Vector2(pickup.position.x, pickup.position.z), here) >= 1.6f) continue;
                pickups.Remove(pickup);
                Destroy(pickup.gameObject);
                Play("pickup");
                if (HasGun) continue;
                HasGun = true;
                Select(Tool.Gun);
            }
        }

        /// What flies off a hit: chips, dust or sparks. `mined` means a tree, rock or wreck rather than a built piece.
        static (Color color, bool sparks) Debris(BuildMaterial material, bool mined) => material switch
        {
            BuildMaterial.Wood => (new Color(0.55f, 0.40f, 0.25f), false),
            // Brick is mined from rock, so a mined hit is grey stone and a built one is red brick.
            BuildMaterial.Brick => (mined ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.62f, 0.30f, 0.22f), false),
            _ => (new Color(1, 0.7f, 0.3f), true),
        };

        /// A short spray of debris thrown back toward the player from `point`.
        void Burst(Vector3 point, (Color color, bool sparks) debris)
        {
            var emitter = new GameObject("Debris") { layer = 2 };
            emitter.transform.position = point;
            emitter.transform.rotation = Quaternion.LookRotation(-player.Eye.forward);
            var spray = emitter.AddComponent<ParticleSystem>();
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = spray.main;
            main.duration = 0.1f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(debris.sparks ? 0.2f : 0.4f, debris.sparks ? 0.45f : 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(debris.sparks ? 3.5f : 1.2f, debris.sparks ? 6.5f : 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(debris.sparks ? 0.01f : 0.02f, debris.sparks ? 0.025f : 0.05f);
            main.startColor = debris.color;
            main.gravityModifier = 1;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = spray.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, debris.sparks ? 18 : 14) });
            var shape = spray.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 60;
            shape.radius = 0.02f;
            var renderer = emitter.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GameAssets.ParticleBase;
            spray.Play();
        }

        void Play(string sound) => voice.PlayOneShot(GameAssets.Sound(sound), 0.9f);
    }
}
