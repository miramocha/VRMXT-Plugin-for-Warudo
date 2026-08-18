VRM 1 spring bone wind — standalone Warudo plugin.

Stock VRM Wind only affects VRM 0.x spring gravity. This plugin adds one
scene Asset that writes UniVRM FastSpringBone model-level ExternalForce
(player 0.130+ ctor via Activator; no ExternalForce field stores).
Springs list: transform path per chain + Freeze toggle. Freeze removes
that chain + ReconstructSpringBone (all physics) until unchecked or Asset
off. Restore spring transforms snaps joints back to load pose.

Export this folder as its own UMod profile (not VRMXT).
