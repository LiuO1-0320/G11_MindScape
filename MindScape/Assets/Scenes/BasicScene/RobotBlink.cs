using System.Collections;
using UnityEngine;

public class RobotBlink : MonoBehaviour
{
    [SerializeField] Transform[] eyes = new Transform[0];
    [SerializeField] SkinnedMeshRenderer face;
    Vector3[] openScales;
    int blinkShape = -1;
    Coroutine blinking;

    void OnEnable()
    {
        if (face != null) blinkShape = face.sharedMesh.GetBlendShapeIndex("Blink");
        openScales = new Vector3[eyes.Length];
        for (int i = 0; i < eyes.Length; i++)
            if (eyes[i] != null) openScales[i] = eyes[i].localScale;
        blinking = StartCoroutine(Blink());
    }

    IEnumerator Blink()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(3.2f, 5.6f));
            for (float t = 0; t < .26f; t += Time.deltaTime)
            {
                float amount = t < .085f ? Mathf.SmoothStep(0, 1, t / .085f)
                    : 1 - Mathf.SmoothStep(0, 1, (t - .105f) / .155f);
                SetClosure(amount);
                yield return null;
            }
            SetClosure(0);
        }
    }

    void SetClosure(float amount)
    {
        if (face != null && blinkShape >= 0)
        {
            face.SetBlendShapeWeight(blinkShape, amount * 100);
            return;
        }
        for (int i = 0; i < eyes.Length; i++)
            if (eyes[i] != null) eyes[i].localScale = Vector3.Scale(openScales[i], new Vector3(1, 1 - .85f * amount, 1));
    }

    void OnDisable()
    {
        if (blinking != null) StopCoroutine(blinking);
        SetClosure(0);
    }
}
