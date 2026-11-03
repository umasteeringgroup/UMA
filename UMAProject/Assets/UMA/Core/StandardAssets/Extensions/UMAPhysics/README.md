# Ragdoll rest

`UMAPhysicsAvatar` automatically freezes settled ragdolls. Configure **Ragdoll rest**
on the Physics Avatar, or on the Physics Slot Definition that creates it.

| Setting | Default | Meaning |
| --- | --- | --- |
| Freeze At Rest | Enabled | Hold the settled pose and stop dynamic simulation. |
| Rest Time | 1 second | Continuous time every body must remain within tolerance. |
| Movement Tolerance | 0.03 meters | Allowed world-space displacement from the start of the rest window. |
| Rotation Tolerance | 5 degrees | Allowed change in body orientation during that window. |

Every registered ragdoll body is checked, including the hips and limbs. Movement
beyond either tolerance restarts the window for the whole ragdoll. This permits
small joint vibrations while preventing a moving limb or slow cumulative sliding
from being mistaken for rest. Tolerances are world-space distances, so adjust them
for unusually small or large characters.

Freezing clears body velocities and makes the bodies kinematic. The avatar keeps
`ragdolled == true`, its Animator stays disabled, and no ragdoll start/end events
fire. Its colliders remain available for raycasts and collisions with other bodies.
`IsRagdollFrozen` exposes the state; the runtime Inspector also shows it.

## Waking and recovery

Call `WakeRagdoll()` before applying a new force or torque:

```csharp
physicsAvatar.WakeRagdoll();
hitBody.AddForceAtPosition(impulse, hitPoint, ForceMode.Impulse);
```

The UMA shooter does this automatically. Custom explosion, dragging, and impact
code must do the same. Kinematic bodies do not wake from ordinary contact or a
direct `Rigidbody.WakeUp()` call. If a floor or platform is about to move or
disappear, wake supported ragdolls first, or disable Freeze At Rest for characters
that must continuously respond to the environment. This follows Unity's
[kinematic body behavior](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody-isKinematic.html).

Waking resumes physics and starts a fresh rest window without restarting animation
or emitting ragdoll start/end events. The Inspector's **Wake Ragdoll** button is
available during Play Mode. Turning off Freeze At Rest also resumes frozen physics
on the next fixed update. Set `ragdolled = false` to perform normal recovery to
animation, including the existing Follow Hips behavior.
