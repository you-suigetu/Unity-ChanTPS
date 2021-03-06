using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class forDebug : MonoBehaviour
{
	[Tooltip("デバックフラグ")]
	public bool debug = false;

	[Tooltip("ゲームスピード")]
	[Range(0.0f, 1.0f)]
	public float GameSpeed = 0.5f;

	// Start is called before the first frame update
	void Awake()
    {
        if (debug == true)
		{
			Time.timeScale = GameSpeed;
		}
    }

    // Update is called once per frame
    void LateUpdate()
	{
		if (debug == true)
		{
			Time.timeScale = GameSpeed;
		}

		if (debug == false)
		{
			Time.timeScale = 1.0f;
		}
	}
}
