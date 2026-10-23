using UnityEngine;

namespace Fortress.Game
{
    /// The arms and what they hold, posed per tool. Everything here is laid out in the iOS
    /// version's SceneKit numbers (right-handed, the view looking down -z) under a parent mirrored
    /// in z, so those numbers carry over unchanged and come out the right way round in Unity.
    public class HeldRig : MonoBehaviour
    {
        /// Where the middle of each fist sits in its arm mesh: the point held things are gripped at.
        static readonly Vector3 Fist = new(0, -0.034f, -0.084f);
        /// Where the fists hold the rifle: the right round the grip, the left under the fore-end.
        static readonly Vector3 RightGrip = new(0.15f, -0.25f, -0.46f), LeftGrip = new(0.15f, -0.22f, -0.8f);

        /// The hands are shrunk toward the eye: they look the same size on screen but stay inside
        /// the player's own body, so they can't reach through a wall.
        const float HandScale = 0.4f;

        Player player;
        Transform hands;
        readonly Transform[] arms = new Transform[2];
        Transform gun, hammer, blueprint, muzzleFlash;
        Tool posed = Tool.Hands;
        float punchClock = 99, swingClock = 99, recoilClock = 99, flashClock = 99;
        int punchArm = 1;

        public void Setup(Player player)
        {
            this.player = player;
            var mirror = new GameObject("Held").transform;
            mirror.SetParent(player.View.transform, false);
            mirror.localScale = new Vector3(1, 1, -1);
            hands = new GameObject("Hands").transform;
            hands.SetParent(mirror, false);
            hands.localScale = Vector3.one * HandScale;

            var gear = GameAssets.Lit(Resources.Load<Texture2D>("Textures/arms_gear"), 0.3f);
            for (int side = 0; side < 2; side++)
            {
                var arm = new GameObject(side == 0 ? "Left arm" : "Right arm");
                arm.AddComponent<MeshFilter>().sharedMesh = GameAssets.RawMesh(side == 0 ? "armL" : "armR");
                arm.AddComponent<MeshRenderer>().sharedMaterial = gear;
                arm.transform.SetParent(hands, false);
                arms[side] = arm.transform;
            }
            gun = BuildGun();
            hammer = BuildHammer();
            blueprint = BuildBlueprint();

            // Everything held is drawn by the held-view camera only, never casts shadows, and has no
            // colliders, so aiming and hits look straight past it.
            foreach (var renderer in mirror.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.gameObject.layer = Player.HeldLayer;
            }
            foreach (var collider in mirror.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            Pose(Tool.Hands, instantly: true);
        }

        /// A rotation given as SceneKit Euler angles in radians: roll about z first, then yaw, then pitch.
        static Quaternion SceneKit(Vector3 angles) =>
            Quaternion.AngleAxis(angles.x * Mathf.Rad2Deg, Vector3.right)
            * Quaternion.AngleAxis(angles.y * Mathf.Rad2Deg, Vector3.up)
            * Quaternion.AngleAxis(angles.z * Mathf.Rad2Deg, Vector3.forward);

        /// Where each arm goes for `tool`, as (position, angles). `side` is -1 left, 1 right.
        static (Vector3, Vector3) ArmPose(Tool tool, float side)
        {
            // Out of sight, low at the side.
            var lowered = (new Vector3(side * 0.3f, -0.45f, -0.36f), new Vector3(0.6f, -side * 0.2f, -side * 1.0f));
            switch (tool)
            {
                case Tool.Hands:
                    return (new Vector3(side * 0.21f, -0.2f, -0.42f), new Vector3(0.3f, -side * 0.28f, -side * 1.0f));
                case Tool.Hammer:
                    return side > 0 ? (new Vector3(0.19f, -0.22f, -0.4f), new Vector3(0.35f, -0.25f, -0.95f)) : lowered;
                case Tool.Gun:
                {
                    // Right fist round the grip, left fist under the fore-end. The angles point each
                    // forearm back and down toward its shoulder, with the fist's grip hole upright.
                    var right = new Vector3(0.49f, 0.3f, -1.57f);
                    var left = new Vector3(0.5f, -0.36f, 1.2f);
                    return side > 0
                        ? (RightGrip - SceneKit(right) * Fist, right)
                        : (LeftGrip - SceneKit(left) * Fist, left);
                }
                default:
                    // Building: the blueprint is in the left hand.
                    return side < 0 ? (new Vector3(-0.17f, -0.27f, -0.4f), new Vector3(0.5f, 0.3f, 0.25f)) : lowered;
            }
        }

        public void Pose(Tool tool, bool instantly = false)
        {
            posed = tool;
            gun.gameObject.SetActive(tool == Tool.Gun);
            hammer.gameObject.SetActive(tool == Tool.Hammer);
            blueprint.gameObject.SetActive(tool.IsPiece());
            swingClock = 99;
            if (!instantly) return;
            for (int i = 0; i < 2; i++)
            {
                var (position, angles) = ArmPose(tool, i == 0 ? -1 : 1);
                arms[i].localPosition = position;
                arms[i].localRotation = SceneKit(angles);
            }
        }

        public void Punch()
        {
            punchArm = 1 - punchArm;
            punchClock = 0;
        }

        /// Winds the hammer back and brings it down. Returns how long until it lands.
        public float Swing()
        {
            swingClock = 0;
            return 0.18f;
        }

        public bool Swinging => swingClock < 0.38f;

        public void Recoil()
        {
            recoilClock = 0;
            flashClock = 0;
        }

        void LateUpdate()
        {
            if (!player) return;
            float dt = Time.deltaTime;
            punchClock += dt;
            swingClock += dt;
            recoilClock += dt;
            flashClock += dt;
            hands.localPosition = player.HandBob;

            float follow = 1 - Mathf.Exp(-dt * 22);
            for (int i = 0; i < 2; i++)
            {
                var (position, angles) = ArmPose(posed, i == 0 ? -1 : 1);
                // A punch jabs one fist forward and back.
                if (i == punchArm && posed == Tool.Hands && punchClock < 0.2f)
                {
                    float reach = punchClock < 0.07f ? punchClock / 0.07f : 1 - (punchClock - 0.07f) / 0.13f;
                    position += new Vector3(0, 0.03f, -0.22f) * reach;
                }
                // A swing winds up, chops down, and comes back.
                if (i == 1 && posed == Tool.Hammer && Swinging)
                {
                    float t = swingClock;
                    angles.x += t < 0.08f ? 0.35f * t / 0.08f
                              : t < 0.18f ? 0.35f - 1.1f * (t - 0.08f) / 0.1f
                              : -0.75f * (1 - (t - 0.18f) / 0.2f);
                    // The swing itself is fast; only easing back into the pose is smoothed.
                    arms[i].localPosition = Vector3.Lerp(arms[i].localPosition, position, follow);
                    arms[i].localRotation = SceneKit(angles);
                    continue;
                }
                arms[i].localPosition = Vector3.Lerp(arms[i].localPosition, position, follow);
                arms[i].localRotation = Quaternion.Slerp(arms[i].localRotation, SceneKit(angles), follow);
            }

            var kick = recoilClock < 0.13f ? (recoilClock < 0.03f ? recoilClock / 0.03f : 1 - (recoilClock - 0.03f) / 0.1f) : 0;
            gun.localPosition = new Vector3(0, 0.02f, 0.08f) * kick;
            muzzleFlash.gameObject.SetActive(flashClock < 0.07f);
        }

        /// Adds a SceneKit-style part: a box (w, h, d) or, when `cylinder` is set, a cylinder of
        /// radius w and height h along y, tilted by SceneKit Euler `tilt`.
        static Transform Part(Transform parent, Material material, Vector3 size, Vector3 position, Vector3 tilt = default, bool cylinder = false)
        {
            var part = new GameObject(cylinder ? "Rod" : "Block");
            part.AddComponent<MeshFilter>().sharedMesh = cylinder ? GameAssets.Cylinder : CubeMesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = SceneKit(tilt);
            // Unity's cylinder is 2 tall with radius 0.5.
            part.transform.localScale = cylinder ? new Vector3(size.x * 2, size.y / 2, size.x * 2) : size;
            return part.transform;
        }

        static Mesh cube;
        static Mesh CubeMesh
        {
            get
            {
                if (cube) return cube;
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = probe.GetComponent<MeshFilter>().sharedMesh;
                Destroy(probe);
                return cube;
            }
        }

        /// The scanned bolt-action rifle, held with its grip in the right fist and its barrel
        /// pointing ahead. Which end of the model is the muzzle is read from the model itself.
        Transform BuildGun()
        {
            // The rifle is an ordinary Unity model, so it sits under a node that undoes the mirror.
            var space = new GameObject("Rifle space").transform;
            space.SetParent(hands, false);
            var rifle = Instantiate(GameAssets.Model("Rifle"), space).transform;
            rifle.name = "Rifle";
            var (bounds, trigger) = RifleLayout(rifle);
            // The trigger sits toward the stock, so the muzzle is the far end from it.
            float muzzleSide = trigger.x > bounds.center.x ? -1 : 1;
            rifle.localRotation = Quaternion.Euler(0, muzzleSide < 0 ? 90 : -90, 0);
            // Mirror back to Unity's handedness: the grip target is in the mirrored numbers, so flip z.
            space.localScale = new Vector3(1, 1, -1);
            var grip = new Vector3(RightGrip.x, RightGrip.y, -RightGrip.z);
            // The hand closes just below and behind the trigger.
            rifle.localPosition = grip - rifle.localRotation * (trigger + new Vector3(muzzleSide * -0.06f, -0.05f, 0));

            var flash = GameAssets.Ghost(new Color(1, 0.85f, 0.4f, 1));
            muzzleFlash = new GameObject("Flash").transform;
            muzzleFlash.gameObject.AddComponent<MeshFilter>().sharedMesh = GameAssets.Sphere;
            muzzleFlash.gameObject.AddComponent<MeshRenderer>().sharedMaterial = flash;
            muzzleFlash.SetParent(rifle, false);
            muzzleFlash.localPosition = new Vector3(muzzleSide > 0 ? bounds.max.x : bounds.min.x, trigger.y + 0.06f, bounds.center.z);
            muzzleFlash.localScale = Vector3.one * 0.08f;
            muzzleFlash.gameObject.SetActive(false);
            space.gameObject.SetActive(false);
            return space;
        }

        /// The rifle model's bounds and trigger position, in the rifle's own space.
        static (Bounds bounds, Vector3 trigger) RifleLayout(Transform rifle)
        {
            var bounds = new Bounds();
            var trigger = Vector3.zero;
            bool first = true;
            foreach (var filter in rifle.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = mesh.center + Vector3.Scale(mesh.extents, new Vector3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1) * 2 - Vector3.one);
                    var point = rifle.InverseTransformPoint(filter.transform.TransformPoint(local));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
                if (filter.name.Contains("trigger")) trigger = rifle.InverseTransformPoint(filter.transform.TransformPoint(mesh.center));
            }
            return (bounds, trigger);
        }

        /// A claw hammer gripped in the right fist, its handle running up and inward from the fist.
        Transform BuildHammer()
        {
            var wood = GameAssets.Art("Planks");
            var steel = GameAssets.Art("Steel");
            var held = new GameObject("Hammer").transform;
            // Arm-mesh space: the handle lies along -x from the fist, the head sits across its far end.
            Part(held, wood, new Vector3(0.016f, 0.36f, 0), new Vector3(-0.1f, 0, 0), new Vector3(0, 0, Mathf.PI / 2), cylinder: true);
            Part(held, steel, new Vector3(0.05f, 0.05f, 0.11f), new Vector3(-0.27f, 0, -0.03f));
            Part(held, steel, new Vector3(0.026f, 0.04f, 0), new Vector3(-0.27f, 0, -0.1f), new Vector3(Mathf.PI / 2, 0, 0), cylinder: true);
            Part(held, steel, new Vector3(0.035f, 0.02f, 0.1f), new Vector3(-0.27f, 0.01f, 0.06f), new Vector3(-0.4f, 0, 0));
            held.SetParent(arms[1], false);
            held.localPosition = Fist;
            held.gameObject.SetActive(false);
            return held;
        }

        /// A blueprint board held up in the left fist while building.
        Transform BuildBlueprint()
        {
            var board = GameAssets.Ghost(Color.white);
            board.SetTexture("_BaseMap", ProceduralTextures.Blueprint());
            board.SetColor("_BaseColor", Color.white);
            var held = new GameObject("Blueprint").transform;
            Part(held, board, new Vector3(0.15f, 0.1f, 0.005f), new Vector3(0, 0.06f, -0.01f), new Vector3(-0.35f, 0, 0));
            held.SetParent(arms[0], false);
            held.localPosition = Fist;
            held.gameObject.SetActive(false);
            return held;
        }
    }
}
