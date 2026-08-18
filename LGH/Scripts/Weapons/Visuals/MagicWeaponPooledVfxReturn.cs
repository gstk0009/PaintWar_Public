using System;
using UnityEngine;

public sealed class MagicWeaponPooledVfxReturn : MonoBehaviour
{
    private GameObject sourcePrefab;
    private float returnAt;
    private Action<GameObject, GameObject> returnAction;
    private bool timedReturnActive;

    public GameObject SourcePrefab { get { return sourcePrefab; } }

    public void SetSource(GameObject source)
    {
        sourcePrefab = source;
    }

    public void Initialize(
        GameObject source,
        float lifetime,
        Action<GameObject, GameObject> newReturnAction)
    {
        sourcePrefab = source;
        returnAction = newReturnAction;
        returnAt = Time.time + Mathf.Max(0.05f, lifetime);
        timedReturnActive = true;
    }

    private void Update()
    {
        if (!timedReturnActive || Time.time < returnAt)
        {
            return;
        }

        timedReturnActive = false;
        if (returnAction != null)
        {
            returnAction(sourcePrefab, gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
