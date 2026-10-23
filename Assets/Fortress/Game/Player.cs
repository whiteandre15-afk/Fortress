using Fortress.Building;
using UnityEngine;

namespace Fortress.Game
{
    /// Walking, sprinting, jumping and crouching on a CharacterController, mouse look, and the
    /// camera: over the shoulder in third person (the default) or at the eyes in first person,
    /// with head bob, kick and landing dip.
    [RequireComponent(typeof(CharacterController))]
    public class Player : MonoBehaviour
    {
        const float EyeHeight = 1.7f, CrouchEyeHeight = 1.1f;
        const float WalkSpeed = 4, SprintFactor = 1.75f;
        /// Upward speed at take-off: about a 1.2m jump against `Gravity`.
        const float JumpSpeed = 5.8f, Gravity = 14;
        const float FieldOfView = 65;
        /// How fast speed builds and drops, in m/s per second: quick on the ground, slow in the air
        /// so a jump keeps its momentum.
        const float Acceleration = 30, Braking = 40, AirControl = 8;
        /// A jump still works this long after walking off an edge, and a press this long before
        /// landing still counts.
        const float JumpGrace = 0.12f;
        /// Where the third-person camera sits relative to the head: right, up, back. Zoomed is closer.
        static readonly Vector3 Shoulder = new(0.95f, 0.6f, -4.2f), ShoulderZoomed = new(0.65f, 0.25f, -1.8f);
        /// How far the camera keeps from walls when the spring arm pulls it in.
        const float CameraRadius = 0.25f;

        /// The player's head, turned to where they're looking. The camera hangs off it.
        public Transform Eye { get; private set; }
        /// Third person (camera over the shoulder) or first person (camera at the eyes). V switches.
        public bool ThirdPerson { get; private set; } = true;
        /// Set by the tools each frame: the body should face where the camera looks (aiming,
        /// building, just after a swing) rather than where it walks.
        public bool Strafing { get; set; }
        /// Set by the tools: in build mode the left trigger builds floors instead of zooming.
        public bool Building { get; set; }
        /// The stand-in body seen in third person (and casting the player's shadow in first).
        public Transform Model { get; private set; }
        /// The rendered camera: rides the eye with bob, kick and dip on top, so those never move the aim.
        public Camera View { get; private set; }
        /// Draws only what's held, on top of the world, so a rifle or hammer never pokes through a wall.
        public Camera HeldView { get; private set; }
        /// The layer held things are on: drawn by `HeldView` only.
        public static int HeldLayer => Mathf.Max(0, LayerMask.NameToLayer("Held"));
        public bool Crouching { get; set; }
        /// For tests: movement input to use instead of the keyboard (x strafe, y forward), and sprint.
        public Vector2? TestInput { get; set; }
        public bool TestSprint { get; set; }
        /// Whether the player counts as standing on something this frame.
        public bool Grounded => body && body.isGrounded;
        /// How many times the player has come back down onto the ground. A walk that keeps
        /// counting these is bouncing.
        public int Landings { get; private set; }
        public bool Paused { get; set; }
        /// Look speed multiplier from the settings menu.
        public float LookSpeed { get; set; } = 1;

        CharacterController body;
        float yaw, pitch, verticalSpeed, eye = EyeHeight, stride, kick, landingDip;
        /// How far back the spring arm currently lets the camera sit, 0 to 1 of the full boom.
        float boom = 1;
        float bodyYaw;
        /// Horizontal velocity, easing toward what the keys ask for.
        Vector3 velocity;
        float sinceGrounded, jumpQueued = -1, bobAmount;
        /// How far the camera is lagging below a step just climbed, easing back to zero.
        float stepLag;
        bool wasGrounded = true;
        AudioSource voice;

        /// Where the camera is, before bob and kick. In third person this is behind the shoulder.
        Vector3 cameraPoint;

        /// The ray through the crosshair that tools and building use. In third person it runs from
        /// the camera, but starts level with the player, so nothing between the camera and the
        /// player (grass, a wall behind them) gets hit or built on.
        public Ray Aim
        {
            get
            {
                if (!ThirdPerson) return new Ray(Eye.position, Eye.forward);
                var forward = Eye.forward;
                float alongToPlayer = Vector3.Dot(Eye.position - cameraPoint, forward);
                return new Ray(cameraPoint + forward * Mathf.Max(0, alongToPlayer), forward);
            }
        }
        /// The storey the player's body is on.
        public int BodyLevel => Mathf.FloorToInt((transform.position.y + 0.9f) / BuildGrid.Storey);

        /// The view direction rounded to a quarter turn: 0 is toward -z, then -x, +z, +x.
        public int Facing
        {
            get
            {
                var forward = Eye.forward;
                var directions = new[] { new Vector2(0, -1), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(1, 0) };
                int best = 0;
                for (int i = 1; i < 4; i++)
                {
                    if (Vector2.Dot(directions[i], new Vector2(forward.x, forward.z)) > Vector2.Dot(directions[best], new Vector2(forward.x, forward.z))) best = i;
                }
                return best;
            }
        }

        public void Setup(AudioSource voice)
        {
            this.voice = voice;
            body = GetComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.3f;
            body.center = new Vector3(0, 0.9f, 0);
            body.stepOffset = 0.45f;
            body.slopeLimit = 50;
            body.skinWidth = 0.03f;

            Eye = new GameObject("Eye").transform;
            Eye.SetParent(transform, false);
            Eye.localPosition = new Vector3(0, EyeHeight, 0);
            var camera = new GameObject("View");
            camera.transform.SetParent(Eye, false);
            View = camera.AddComponent<Camera>();
            View.nearClipPlane = 0.05f;
            View.farClipPlane = 450;
            View.fieldOfView = FieldOfView;
            camera.AddComponent<AudioListener>();
            camera.tag = "MainCamera";
            View.cullingMask = ~(1 << HeldLayer);

            var held = new GameObject("Held view");
            held.transform.SetParent(camera.transform, false);
            HeldView = held.AddComponent<Camera>();
            HeldView.nearClipPlane = 0.01f;
            HeldView.farClipPlane = 10;
            HeldView.fieldOfView = 55;
            HeldView.cullingMask = 1 << HeldLayer;
            var heldData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(HeldView);
            heldData.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
            var viewData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(View);
            viewData.cameraStack.Add(HeldView);
            // Post-processing runs on the last camera of the stack, so it grades the world and the held things together.
            viewData.renderPostProcessing = false;
            heldData.renderPostProcessing = true;

            // A stand-in body until the real character is in.
            var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.name = "Body";
            Destroy(model.GetComponent<Collider>());
            model.layer = 2;
            model.transform.SetParent(transform, false);
            model.transform.localPosition = new Vector3(0, 0.9f, 0);
            model.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            model.GetComponent<MeshRenderer>().sharedMaterial = GameAssets.Lit(new Color(0.28f, 0.32f, 0.22f), 0.25f);
            Model = model.transform;
            ThirdPerson = PlayerPrefs.GetInt("thirdPerson", 1) == 1;
            ApplyView();
        }

        /// Shows the body and hides the held arms in third person, and the other way round in first,
        /// where the body still casts its shadow.
        void ApplyView()
        {
            HeldView.enabled = !ThirdPerson;
            foreach (var renderer in Model.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = ThirdPerson ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        /// Turns to face `degrees` around the vertical (0 looks along +z) and `tilt` degrees up.
        public void Face(float degrees, float tilt)
        {
            yaw = bodyYaw = degrees;
            pitch = -tilt;
        }

        /// Kicks the view up a little, from a hit or a shot.
        public void Kick(float degrees) => kick += degrees;

        void Update()
        {
            if (Paused) return;
            float dt = Time.deltaTime;
            var look = Controls.Look(dt) * LookSpeed;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -85, 85);
            var heading = Quaternion.Euler(0, yaw, 0);

            var input = Controls.Move;
            bool sprintKey = Controls.Sprint, jump = Controls.Jump;
            if (Controls.Crouch) Crouching = !Crouching;
            if (Controls.SwitchView)
            {
                ThirdPerson = !ThirdPerson;
                PlayerPrefs.SetInt("thirdPerson", ThirdPerson ? 1 : 0);
                ApplyView();
            }
            if (TestInput is { } driven)
            {
                input = driven;
                sprintKey = TestSprint;
            }
            input = Vector2.ClampMagnitude(input, 1);
            bool running = sprintKey && !Crouching && input.y > 0.1f;
            float speed = WalkSpeed * (Crouching ? 0.5f : running ? SprintFactor : 1);
            // Movement follows the camera's heading, whichever way the body is turned.
            var target = (heading * Vector3.right * input.x + heading * Vector3.forward * input.y) * speed;
            bool grounded = body.isGrounded;
            float rate = !grounded ? AirControl : target == Vector3.zero ? Braking : Acceleration;
            velocity = Vector3.MoveTowards(velocity, target, rate * dt);

            if (grounded)
            {
                if (!wasGrounded) Landings++;
                if (!wasGrounded && verticalSpeed < -4)
                {
                    landingDip = Mathf.Min(0.12f, -verticalSpeed * 0.012f);
                    voice.PlayOneShot(GameAssets.Sound("step"), 0.35f);
                }
                sinceGrounded = 0;
                if (verticalSpeed < 0) verticalSpeed = -2;
            }
            else
            {
                sinceGrounded += dt;
                verticalSpeed -= Gravity * dt;
            }
            jumpQueued = jump ? JumpGrace : jumpQueued - dt;
            if (jumpQueued > 0 && sinceGrounded < JumpGrace && verticalSpeed <= 0)
            {
                verticalSpeed = JumpSpeed;
                jumpQueued = -1;
                sinceGrounded = JumpGrace;
            }
            wasGrounded = grounded;
            body.height = Crouching ? 1.25f : 1.8f;
            body.center = new Vector3(0, body.height / 2, 0);
            var before = transform.position;
            body.Move((velocity + Vector3.up * verticalSpeed) * dt);
            // Climbing onto a slab or rock lifts the body in one frame; let the camera catch up instead.
            // A step rises far more steeply than any slope or ramp (ramps are about 47 degrees, tan 1.08).
            var moved = transform.position - before;
            float across = new Vector2(moved.x, moved.z).magnitude;
            if (grounded && moved.y > 0.03f && moved.y > across * 1.4f) stepLag += moved.y;
            stepLag -= stepLag * Mathf.Min(dt * 14, 1);

            // The body turns to face where it walks, or where the camera looks when aiming or in first person.
            var flat = new Vector2(velocity.x, velocity.z);
            float wantYaw = !ThirdPerson || Strafing ? yaw : flat.magnitude > 0.5f ? Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg : bodyYaw;
            bodyYaw = !ThirdPerson ? yaw : Mathf.MoveTowardsAngle(bodyYaw, wantYaw, 720 * dt);
            transform.rotation = Quaternion.Euler(0, bodyYaw, 0);

            eye += ((Crouching ? CrouchEyeHeight : EyeHeight) - eye) * Mathf.Min(dt * 10, 1);
            Eye.localPosition = new Vector3(0, eye - stepLag, 0);
            Eye.rotation = Quaternion.Euler(pitch, yaw, 0);
            Model.localScale = new Vector3(0.6f, (Crouching ? 0.62f : 0.9f), 0.6f);
            Model.localPosition = new Vector3(0, Crouching ? 0.62f : 0.9f, 0);

            bool zooming = Controls.Zoom(Building);
            float moving = new Vector2(velocity.x, velocity.z).magnitude;
            float wanted = zooming ? (ThirdPerson ? 50 : 40) : FieldOfView + (moving > WalkSpeed * 1.1f ? 8 : 0);
            View.fieldOfView += (wanted - View.fieldOfView) * Mathf.Min(dt * 10, 1);

            // Head bobs with each step, by how fast the player is really going, easing in and out.
            float pace = moving / WalkSpeed;
            float lastStride = stride;
            stride += dt * 9 * Mathf.Min(pace, 1) * (pace > 1.1f ? 1.4f : Crouching ? 0.7f : 1);
            if ((int)(stride / Mathf.PI) != (int)(lastStride / Mathf.PI) && grounded)
                voice.PlayOneShot(GameAssets.Sound("step"), 0.35f);
            bobAmount += ((grounded ? Mathf.Min(pace * 2, 1) : 0) - bobAmount) * Mathf.Min(dt * 8, 1);
            float bob = bobAmount;
            landingDip -= landingDip * Mathf.Min(dt * 6, 1);
            kick -= kick * Mathf.Min(dt * 12, 1);
            var shake = new Vector3(Mathf.Sin(stride) * 0.02f * bob, (Mathf.Abs(Mathf.Cos(stride)) - 0.5f) * 0.04f * bob - landingDip, 0);
            var offset = ThirdPerson ? SpringArm(zooming ? ShoulderZoomed : Shoulder, dt) : Vector3.zero;
            cameraPoint = Eye.TransformPoint(offset);
            // Third person keeps only a touch of the head bob: the body does the walking.
            View.transform.localPosition = offset + shake * (ThirdPerson ? 0.3f : 1);
            View.transform.localRotation = Quaternion.Euler(-kick, 0, 0);
        }

        /// The camera's offset from the head, pulled in wherever something's in the way, so it never
        /// ends up inside a wall, tree, rock or hill. Pulls in at once, eases back out.
        Vector3 SpringArm(Vector3 wanted, float dt)
        {
            var pivot = Eye.position;
            var end = Eye.TransformPoint(wanted);
            var direction = end - pivot;
            float reach = 1;
            int solid = Physics.DefaultRaycastLayers & ~(1 << 2) & ~(1 << HeldLayer);
            if (Physics.SphereCast(pivot, CameraRadius, direction.normalized, out var hit, direction.magnitude, solid, QueryTriggerInteraction.Ignore))
                reach = Mathf.Max(0.05f, hit.distance / direction.magnitude);
            boom = reach < boom ? reach : Mathf.MoveTowards(boom, reach, dt * 2.5f);
            return wanted * boom;
        }

        /// Where the camera is right now, for tests.
        public Vector3 CameraPosition => View.transform.position;

        /// The hand bob offset for the held rig, in metres.
        public Vector3 HandBob
        {
            get
            {
                return new Vector3(Mathf.Sin(stride) * 0.008f, Mathf.Abs(Mathf.Cos(stride)) * 0.014f * bobAmount, 0);
            }
        }
    }
}
