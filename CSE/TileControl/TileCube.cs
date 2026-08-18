using UnityEngine;

public class TileCube : MonoBehaviour
{
    //private void OnTriggerEnter(Collider other)
    //{
    //    if (other.CompareTag("Player"))
    //    {
    //        if (TilePaintManager.HasInstance)
    //        {
    //            TilePaintManager.Instance.TryClaim(this, other);
    //            return;
    //        }

    //        Player playerScript = other.GetComponentInParent<Player>();
    //        if (playerScript != null)
    //        {
    //            Material mat = playerScript.GetPlayerMaterialColor();
    //            GetComponent<Renderer>().material = mat;
    //        }
    //        else
    //        {
    //            Debug.Log("Player script is null.");
    //        }
    //    }
    //}
}
