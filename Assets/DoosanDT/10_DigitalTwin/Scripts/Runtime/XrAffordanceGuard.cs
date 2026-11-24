using System;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Desktop / non-XR play: disable XRI Affordance providers and receivers that throw
    /// NullReferenceException in BaseAsyncAffordanceStateReceiver.HandleTween when
    /// theme/target refs are missing. Mount on a scene-resident object (e.g. ROS2).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class XrAffordanceGuard : MonoBehaviour
    {
        private void Awake()
        {
            ApplyGuard("Awake");
        }

        private void Start()
        {
            // Second pass: catch affordances enabled/spawned after Awake.
            ApplyGuard("Start");
        }

        private void ApplyGuard(string phase)
        {
            int closedProviders = 0;
            int closedReceivers = 0;
            try
            {
                var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < all.Length; i++)
                {
                    var mb = all[i];
                    if (mb == null || !mb.enabled || ReferenceEquals(mb, this))
                    {
                        continue;
                    }

                    string full = mb.GetType().FullName ?? mb.GetType().Name;
                    bool isProvider = full.IndexOf("AffordanceStateProvider", StringComparison.Ordinal) >= 0;
                    bool isAsyncReceiver = full.IndexOf("AsyncAffordanceStateReceiver", StringComparison.Ordinal) >= 0
                                           || full.IndexOf("BaseAsyncAffordanceStateReceiver", StringComparison.Ordinal) >= 0;

                    // Desktop non-VR: hover affordance tweens are unused — disable every provider.
                    if (isProvider)
                    {
                        mb.enabled = false;
                        closedProviders++;
                        continue;
                    }

                    // Missing-ref async receivers NRE in HandleTween; on desktop disable all of them
                    // so a half-wired theme cannot keep ticking after providers are off.
                    if (isAsyncReceiver)
                    {
                        mb.enabled = false;
                        closedReceivers++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XrAffordanceGuard] " + phase + ": " + ex.Message, this);
                return;
            }

            int closed = closedProviders + closedReceivers;
            if (closed > 0)
            {
                Debug.Log(
                    "[XrAffordanceGuard] " + phase + ": disabled " + closed
                    + " XR affordance component(s) (providers=" + closedProviders
                    + ", receivers=" + closedReceivers + ").",
                    this);
            }
        }
    }
}
