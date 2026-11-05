using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRPracticas
{
    /// <summary>
    /// Botón, perilla o palanca de un electrodoméstico de la cafetería.
    /// El Poke (InteractableUnityEventWrapper > When Select) llama a Press() y el control alterna entre apagado y encendido.
    /// </summary>
    public class CafeButton : MonoBehaviour
    {
        const float PartDegreesPerSecond = 360f;
        const float DoorDegreesPerSecond = 90f;

        // Varios botones pueden encender la misma pieza (la pantalla del microondas): sigue encendida mientras alguno la use.
        static readonly Dictionary<Renderer, int> GlowUsers = new Dictionary<Renderer, int>();

        [Header("Control")]
        [Tooltip("Pieza que gira mientras está encendido (perilla o palanca). Opcional.")]
        public Transform movingPart;
        public Vector3 onRotation;
        [Tooltip("Segundos hasta apagarse solo. Con 0 queda encendido hasta volver a presionarlo.")]
        public float autoOffSeconds;
        public AudioClip pressSound;

        [Header("Lo que enciende")]
        public Renderer[] glowRenderers;
        public Material glowMaterial;
        public ParticleSystem[] particles;
        public Transform door;
        public Vector3 doorOpenRotation;

        public bool IsOn { get; private set; }

        Material[] _offMaterials;
        Quaternion _partOff;
        Quaternion _doorOff;

        void Awake()
        {
            if (movingPart)
                _partOff = movingPart.localRotation;
            if (door)
                _doorOff = door.localRotation;

            _offMaterials = new Material[glowRenderers.Length];
            for (int i = 0; i < glowRenderers.Length; i++)
                _offMaterials[i] = glowRenderers[i].sharedMaterial;
        }

        void Update()
        {
            if (movingPart)
            {
                var target = IsOn ? _partOff * Quaternion.Euler(onRotation) : _partOff;
                movingPart.localRotation = Quaternion.RotateTowards(movingPart.localRotation, target, PartDegreesPerSecond * Time.deltaTime);
            }

            if (door)
            {
                var target = IsOn ? _doorOff * Quaternion.Euler(doorOpenRotation) : _doorOff;
                door.localRotation = Quaternion.RotateTowards(door.localRotation, target, DoorDegreesPerSecond * Time.deltaTime);
            }
        }

        public void Press()
        {
            if (pressSound)
                AudioSource.PlayClipAtPoint(pressSound, transform.position, 0.7f);

            SetOn(!IsOn);
        }

        void SetOn(bool on)
        {
            IsOn = on;
            StopAllCoroutines();

            for (int i = 0; i < glowRenderers.Length; i++)
            {
                GlowUsers.TryGetValue(glowRenderers[i], out int users);
                users += on ? 1 : -1;
                GlowUsers[glowRenderers[i]] = users;
                glowRenderers[i].sharedMaterial = users > 0 ? glowMaterial : _offMaterials[i];
            }

            foreach (var system in particles)
            {
                if (on)
                    system.Play();
                else
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (on && autoOffSeconds > 0f)
                StartCoroutine(TurnOffLater());
        }

        IEnumerator TurnOffLater()
        {
            yield return new WaitForSeconds(autoOffSeconds);
            SetOn(false);
        }
    }
}
