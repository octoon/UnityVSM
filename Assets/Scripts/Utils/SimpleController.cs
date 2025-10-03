using UnityEngine;

namespace VSM.Utils
{
    /// <summary>
    /// Simple camera controller for testing VSM
    /// </summary>
    public class SimpleController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float lookSpeed = 2.0f;
        [SerializeField] private float sprintMultiplier = 2.0f;

        private float rotationX = 0;
        private float rotationY = 0;

        void Start()
        {
            Vector3 rot = transform.localRotation.eulerAngles;
            rotationY = rot.y;
            rotationX = rot.x;
        }

        void Update()
        {
            HandleMovement();
            HandleLook();
        }

        void HandleMovement()
        {
            float speed = moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
            {
                speed *= sprintMultiplier;
            }

            Vector3 movement = Vector3.zero;

            if (Input.GetKey(KeyCode.W))
                movement += transform.forward;
            if (Input.GetKey(KeyCode.S))
                movement -= transform.forward;
            if (Input.GetKey(KeyCode.A))
                movement -= transform.right;
            if (Input.GetKey(KeyCode.D))
                movement += transform.right;
            if (Input.GetKey(KeyCode.E))
                movement += Vector3.up;
            if (Input.GetKey(KeyCode.Q))
                movement -= Vector3.up;

            transform.position += movement * speed * Time.deltaTime;
        }

        void HandleLook()
        {
            if (Input.GetMouseButton(1))  // Right mouse button
            {
                rotationY += Input.GetAxis("Mouse X") * lookSpeed;
                rotationX -= Input.GetAxis("Mouse Y") * lookSpeed;
                rotationX = Mathf.Clamp(rotationX, -90, 90);

                transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
            }
        }
    }
}
