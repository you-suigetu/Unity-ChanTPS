using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MyTPCSys
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class MyTPC : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("歩行速度 m/s")]
		public float MoveSpeed = 2.0f;
		[Tooltip("ダッシュ速度 m/s")]
		public float SprintSpeed = 5.0f;
		[Tooltip("旋回レート")]
		[Range(0.0f, 0.3f)]
		public float RotationSmoothTime = 0.12f;
		[Tooltip("速度ピッチ")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("跳躍高さ")]
		public float JumpHeight = 1.2f;
		[Tooltip("キャラクターに影響する重力 エンジンのデフォルトは-9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("ジャンプクールタイム")]
		public float JumpTimeout = 0.50f;
		[Tooltip("落下クールタイム(微妙な段差で落下モーションに入らないため)")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("接地判定フラグ (CharacterControllerの接地判定とは別)")]
		public bool Grounded = true;
		[Tooltip("地面粗さ")]
		public float GroundedOffset = -0.14f;
		[Tooltip("接地に使用する半径 CharacterControllerの値と同値にすること")]
		public float GroundedRadius = 0.28f;
		[Tooltip("地面として認識するレイヤー")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("CinemachineVirtualCameraがフォローするカメラオブジェクト")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("カメラの最大俯角")]
		public float TopClamp = 70.0f;
		[Tooltip("カメラの最大仰角")]
		public float BottomClamp = -30.0f;
		[Tooltip("カメラをオーバーライドするための追加角度 ロック時にカメラの位置を微調整するのに使用")]
		public float CameraAngleOverride = 0.0f;
		[Tooltip("カメラ位置ロックフラグ")]
		public bool LockCameraPosition = false;

		// cinemachine
		private float _cinemachineTargetYaw;
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _animationBlend;
		private float _targetRotation = 0.0f;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

		// animation IDs
		private int _animIDSpeed;
		private int _animIDGrounded;
		private int _animIDJumping;
		private int _animIDFreeFall;
		private int _animIDMotionSpeed;

		private Animator _animator;
		private CharacterController _controller;
		private TPCInput _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		private bool _hasAnimator;

		//追加分
		private bool _isJumping;
		private bool _isFreeFall;
		private bool _isLanding;

		private bool _isJumpImpulse;

		private int _animIDLanding;

		private float _jumpVelocity;

		private string _animNameJumping;
		private string _animNameLandingEnd;

		private void Awake()
		{
			//メインカメラの取得
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_hasAnimator = TryGetComponent(out _animator);
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<TPCInput>();

			AssignAnimationNames();

			//クールタイムをセット
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;

			GroundedCheck();

			if (_hasAnimator == true)
			{
				var clips = _animator.runtimeAnimatorController.animationClips;

				//Debug.Log("Animation Clipの数 : " + clips.Length);

				//for (int i = 0; i < clips.Length; i++)
				//{
				//	string stateName = clips[i].name;
				//	Debug.Log(stateName + " " + i);
				//}

				_animNameJumping = clips[4].name;
				_animNameLandingEnd = clips[5].name;
			}
		}

		private void Update()
		{
			_hasAnimator = TryGetComponent(out _animator);

			JumpAndGravity();
			GroundedCheck();
			Move();

			if (_animator.GetCurrentAnimatorClipInfo(0)[0].clip.name == _animNameJumping)
			{
				_isJumping = true;
			}

			if (_animator.GetCurrentAnimatorClipInfo(0)[0].clip.name == _animNameLandingEnd)
			{
				_isLanding = false;
			}


		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void AssignAnimationNames()
		{
			if (_hasAnimator == true)
			{
				_animIDSpeed = Animator.StringToHash("Speed");
				_animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
				_animIDGrounded = Animator.StringToHash("Grounded");
				_animIDJumping = Animator.StringToHash("Jumping");
				_animIDFreeFall = Animator.StringToHash("FreeFall");
				_animIDLanding = Animator.StringToHash("Landing");
			}
		}

		private void GroundedCheck()
		{
			//球の位置をオフセット付きで設定
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);

			AnimatorUpdate();
		}

		private void CameraRotation()
		{
			//入力があり、カメラの位置が固定されていない場合
			if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
			{
				_cinemachineTargetYaw += _input.look.x * Time.deltaTime;
				_cinemachineTargetPitch += _input.look.y * Time.deltaTime;
			}

			//値が360度に制限されるよう回転をクランプ
			_cinemachineTargetYaw = ClampAngle(_cinemachineTargetYaw, float.MinValue, float.MaxValue);
			_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

			//Cinemachineはこのターゲットに追従
			CinemachineCameraTarget.transform.rotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride, _cinemachineTargetYaw, 0.0f);
		}

		private void Move()
		{
			float targetSpeed;

			if (_input.move == Vector2.zero)//Vector2の==演算子は近似を使用するため浮動小数点エラーが発生しにくい
			{
				targetSpeed = 0.0f; //移動入力がない場合は速度を0にセット
			}

			else
			{
				//ダッシュ入力を判別して移動速度セット
				if (_input.sprint == false)
				{
					targetSpeed = MoveSpeed;
				}

				else
				{
					targetSpeed = SprintSpeed;
				}
			}

			//プレーヤーの現在の水平速度を参照 
			float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			//目標速度まで加速または減速 
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				//線形ではなく曲線の結果を作成し、より有機的な速度変化をもたらす
				//Lerp内のTimeはクランプされており、速度をクランプする必要はない
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

				//小数点以下第3位で速度丸め
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}

			else
			{
				_speed = targetSpeed;
			}

			_animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);

			//入力方向を正規化
			Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

			//キャラクターの向き修正
			if (_input.move != Vector2.zero)
			{
				_targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + _mainCamera.transform.eulerAngles.y;
				float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);

				//カメラの位置を基準にして入力方向を向くように回転
				transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
			}

			//移動先の算出
			Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;

			//キャラクターの移動
			_controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

			if (_hasAnimator)
			{
				_animator.SetFloat(_animIDSpeed, _animationBlend);
				_animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
			}
		}

		private void JumpAndGravity()
		{
			if (Grounded == true)
			{
				if (_isFreeFall == true) //落下処理中
				{
					_isFreeFall = false;
					_isLanding = true; //着地モーションに移行
					AnimatorUpdate();
				}

				//落下クールタイムをリセット
				_fallTimeoutDelta = FallTimeout;

				_isJumping = false;
				_isFreeFall = false;
				AnimatorUpdate();

				//接地時にかかる重力を一定に保つため
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				//ジャンプ判定
				if (_input.jump == true && _isLanding == false && _jumpTimeoutDelta <= 0.0f)
				{
					//目標高さに到達するために必要な速度を算出
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

					_isJumping = true; //ジャンプモーションへの移行フラグ成立

					AnimatorUpdate();
				}

				//ジャンプのクールタイム処理
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}

			else
			{
				//ジャンプのクールタイム再セット
				_jumpTimeoutDelta = JumpTimeout;

				////微小な段差での落下モーション移行対策
				//if (_fallTimeoutDelta >= 0.0f)
				//{
				//	_fallTimeoutDelta -= Time.deltaTime;
				//}

				//else
				//{
				//	_isFreeFall = true;
				//	AnimatorUpdate();
				//}

				//微小な段差での落下モーション移行対策
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				else
				{
					_isFreeFall = true;
					AnimatorUpdate();
				}

				//空中にいる間はジャンプ入力をオフに
				_input.jump = false;
			}

			//ターミナル下にある場合、時間の経過とともに重力を適用
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}

			if ((_verticalVelocity <= 0) && (_isJumping == true)) //ジャンプから落下に移行
			{
				_isJumping = false;
				_isFreeFall = true;
				AnimatorUpdate();
			}
		}

		private void AnimatorUpdate()
		{
			if (_hasAnimator == true)
			{
				_animator.SetBool(_animIDGrounded, Grounded);
				_animator.SetBool(_animIDJumping, _isJumping);
				_animator.SetBool(_animIDFreeFall, _isFreeFall);
				_animator.SetBool(_animIDLanding, _isLanding);
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;
			
			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}