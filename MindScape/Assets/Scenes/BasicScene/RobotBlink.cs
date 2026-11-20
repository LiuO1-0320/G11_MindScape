using System.Collections;
using UnityEngine;

public class RobotBlink : MonoBehaviour
{
    [SerializeField] Transform[] eyes = new Transform[0];

    IEnumerator Start()
    {
        var sizes = new Vector3[eyes.Length];
        for (int i = 0; i < eyes.Length; i++) sizes[i] = eyes[i].localScale;
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(2.8f, 5.2f));
            for (float t = 0; t < .2f; t += Time.deltaTime)
            {
                float height = 1 - .85f * Mathf.Sin(Mathf.PI * t / .2f);
                for (int i = 0; i < eyes.Length; i++)
                    eyes[i].localScale = Vector3.Scale(sizes[i], new Vector3(1, height, 1));
                yield return null;
            }
            for (int i = 0; i < eyes.Length; i++) eyes[i].localScale = sizes[i];
        }
    }
}
