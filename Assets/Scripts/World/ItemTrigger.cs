using System;
using UnityEngine;

/// <summary>
/// Lightweight child component that forwards trigger callbacks up to the parent.
/// Attach to the child GameObject alongside its trigger Collider2D.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemTrigger : MonoBehaviour
{
    public event Action<Collider2D> OnTriggerEntered;
    public event Action<Collider2D> OnTriggerExited;

    private void OnTriggerEnter2D(Collider2D other) => OnTriggerEntered?.Invoke(other);
    private void OnTriggerExit2D(Collider2D other)  => OnTriggerExited?.Invoke(other);
}