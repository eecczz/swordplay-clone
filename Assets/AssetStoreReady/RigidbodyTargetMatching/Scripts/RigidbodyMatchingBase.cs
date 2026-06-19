using UnityEngine;

[System.Serializable]
public class EnableAxes
{
    public bool X = true;
    public bool Y = true;
    public bool Z = true;

    public bool One
    {
        get => X || Y || Z;
    }
}

[System.Serializable]
public class ForceParams
{
    public float Spring = 1000;
    public float Damper = 50;
    [Tooltip("The larger the value, the stronger the pressure on the other object")]
    public float ForceLimit = Mathf.Infinity;
    [Tooltip("Axes along which the force will act")]
    public EnableAxes EnableAxes;
}

[RequireComponent(typeof(Rigidbody))]
public abstract class RigidbodyMatchingBase : MonoBehaviour
{
    [Tooltip("Use ForceMode.Acceleration instead ForceMode.Force")]
    public bool IgnoreMass;
    public ForceParams PositionDrive;
    public ForceParams RotationDrive;

    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

    protected Transform _transform;
    protected Rigidbody _rigidbody;

    void Awake()
    {
        _transform = transform;
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.maxAngularVelocity = Mathf.Infinity;
    }

    public void SetTargetPlacement(Vector3 position, Quaternion rotation)
    {
        _targetPosition = position;
        _targetRotation = rotation;
    }
    public void SetTargetPosition(Vector3 position)
    {
        _targetPosition = position;
    }
    public void SetTargetRotation(Quaternion rotation)
    {
        _targetRotation = rotation;
    }
    public void SetTargetPlacement(Transform target)
    {
        _targetPosition = target.position;
        _targetRotation = target.rotation;
    }

    protected virtual void FixedUpdate()
    {
        UpdatePosition();
        UpdateRotation();
    }

    private Vector3 DisableAxes(Vector3 vector, EnableAxes axes)
    {
        Vector3 newVector = vector;
        if (!axes.X) newVector.x = 0;
        if (!axes.Y) newVector.y = 0;
        if (!axes.Z) newVector.z = 0;
        return newVector;
    }

    protected virtual void UpdatePosition()
    {
        ForceMode forceMode = IgnoreMass ? ForceMode.Acceleration : ForceMode.Force;

        if (PositionDrive.EnableAxes.One)
        {
            Vector3 force = CalculateForce(_targetPosition);
            force = Vector3.ClampMagnitude(force, PositionDrive.ForceLimit);
            force = DisableAxes(force, PositionDrive.EnableAxes);

            _rigidbody.AddForce(force, forceMode);
        }
    }

    protected virtual void UpdateRotation()
    {
        ForceMode forceMode = IgnoreMass ? ForceMode.Acceleration : ForceMode.Force;

        if (RotationDrive.EnableAxes.One)
        {
            Vector3 torque = CalculateTorque(_targetRotation);
            torque = Vector3.ClampMagnitude(torque, RotationDrive.ForceLimit);
            torque = DisableAxes(torque, RotationDrive.EnableAxes);

            _rigidbody.AddTorque(torque, forceMode);
        }
    }

    private Vector3 CalculateForce(Vector3 targetPosition)
    {
        Vector3 vector = targetPosition - _rigidbody.position;
        Vector3 springForce = vector * PositionDrive.Spring;
        Vector3 damperForce = _rigidbody.linearVelocity * PositionDrive.Damper;
        Vector3 force = springForce - damperForce;
        return force;
    }

    private Vector3 CalculateTorque(Quaternion targetRotation)
    {
        Quaternion deltaRot = targetRotation * Quaternion.Inverse(_rigidbody.rotation);
        deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
        Vector3 angleDelta = axis * angle * Mathf.Deg2Rad;

        Vector3 springForce = angleDelta * RotationDrive.Spring;
        Vector3 damperForce = _rigidbody.angularVelocity * RotationDrive.Damper;

        Vector3 torqueLocal = _transform.InverseTransformDirection(springForce - damperForce);
        torqueLocal = _rigidbody.inertiaTensorRotation * torqueLocal;
        torqueLocal.Scale(_rigidbody.inertiaTensor);
        torqueLocal = Quaternion.Inverse(_rigidbody.inertiaTensorRotation) * torqueLocal;
        Vector3 torque = _transform.TransformDirection(torqueLocal);
        return torque;
    }
}
