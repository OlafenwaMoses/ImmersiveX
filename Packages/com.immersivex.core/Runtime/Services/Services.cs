using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImmersiveX
{
    /// <summary>
    /// A tiny service registry: platform adapters register providers by interface, features look them up.
    /// No dependency-injection framework (see ADR-0004).
    /// </summary>
    public static class Services
    {
        static readonly Dictionary<Type, object> Registered = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            Registered[typeof(T)] = service;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Registered.TryGetValue(typeof(T), out var found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        public static T Get<T>() where T : class =>
            TryGet<T>(out var service)
                ? service
                : throw new InvalidOperationException($"No {typeof(T).Name} is registered. The active platform adapter doesn't provide one.");

        public static void Clear() => Registered.Clear();

        // Play mode can start without a domain reload, so clear static state explicitly.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();
    }
}
