using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ThirdPersonCharacterControl : MonoBehaviour
{
    private Rigidbody rig;

    //sprint
    static float Speed = 10f;
    float SprintSpeed = 17.5f;
    //Jetpack
    float jetpackForce = 5.0f;
    static float maxFuel = 100.0f;
    float currentFuel = maxFuel;
    float chargeFuel = 70.0f;
    //Jump
    float jumpVelocity = 7.5f;
    private float distToGround = 1.0f;
    bool isGrounded = false;
    //Move
    private Vector3 inputVector;

    void Start()
    {
        rig = GetComponent<Rigidbody>();
    }

    void Update()
    {
        Jetpack();
        Sprint();
        Jump();
        Move();
    }

    void Jetpack()
    {
        bool jetpackActive = Input.GetButton("Jetpack");

        if (jetpackActive && currentFuel > 0)
        {
            rig.AddForce(new Vector3(0, jetpackForce, 0), ForceMode.Acceleration);

            currentFuel -= chargeFuel * Time.deltaTime;
        }
        else if (!jetpackActive)
        {
            rig.velocity *= 1.0f;

            if(isGrounded && currentFuel < maxFuel)
            {
                currentFuel += chargeFuel * Time.deltaTime * 2;
            }
        }
    }
    void Sprint()
    {
        bool sprintActive = Input.GetButton("Sprint");
        
       if (sprintActive)
       {
            Speed = SprintSpeed;
       }
        else if (!sprintActive)
        {
            Speed = 5.0f;
        }
        
    }
    void Jump()
    {
        GroundCheck();
        if (isGrounded)
        {
            if (Input.GetButtonDown("Jump"))
            {
                rig.velocity = Vector3.up * jumpVelocity;
            }
        }
    }
    void Move()
    {
        inputVector = new Vector3(Input.GetAxis("Horizontal") * Speed, rig.velocity.y, 0.0f);
        rig.velocity = inputVector;
    }
    void GroundCheck()
    {
        if (Physics.Raycast(transform.position, Vector3.down, distToGround + 0.1f))
        {
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
    }

}