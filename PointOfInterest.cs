using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(Renderer))]
public class PointOfInterest : MonoBehaviour
{
    [Tooltip("Main camera to which we calculate visibility percentages")]
    public Camera mainCamera;

    [Tooltip("Number of Raycasts to perform")]
    public int precision = 50;

    [Tooltip("Enable raycasts to be drawn")]
    public bool drawRays;

    [Tooltip("Font size for visibility percentage")]
    public int fontSize = 12;

    [Tooltip("Colour for visiblity percentage font")]
    public Color fontColour = Color.white;
    
    private MeshFilter meshFilter;
    private Renderer renderer;
    private Mesh mesh;
    private Vector3[] vertices;
    private float visibilityPercentage;
    private Vector3 cameraPosition;
    private bool isVisible;
    private bool isOutsideCameraView;
    
    private List<Triangle> triangles;
    private List<float> triangleSurfaceAreas;
    
    private List<Triangle> visibleTriangles;
    private List<float> visibleSurfaceAreas;

    private List<Vector3> surfacePoints;

    private void OnDrawGizmos()
    { 
        GUIStyle guiStyle = new GUIStyle();
        guiStyle.fontSize = fontSize;
        guiStyle.normal.textColor = fontColour;
        Handles.Label(transform.position, (visibilityPercentage * 100) + "%", guiStyle);
    }

    private void Update()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (renderer == null)
        {
            renderer = GetComponent<Renderer>();
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        triangleSurfaceAreas = new List<float>();
        visibleTriangles = new List<Triangle>();
        visibleSurfaceAreas = new List<float>();
        surfacePoints = new List<Vector3>();
        triangles = new List<Triangle>();
        
        Initialize();
        DetermineOutsideCameraFrustum();
        
        if (!isOutsideCameraView)
        {
            CalculateRoughVisibility();
        }
        else
        {
            isVisible = false;
        }
        
        if (isVisible)
        {
            CalculateVisibleTriangles();
            CollectUniformSampledSurfacePoints();
            PerformRaycasts();
        }
    }
    
    void Initialize()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (renderer == null)
        {
            renderer = GetComponent<Renderer>();
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        triangleSurfaceAreas = new List<float>();
        visibleTriangles = new List<Triangle>();
        visibleSurfaceAreas = new List<float>();
        surfacePoints = new List<Vector3>();
        triangles = new List<Triangle>();
        
        cameraPosition = mainCamera.transform.position;
        visibilityPercentage = 0;
        isVisible = false;

        Mesh mesh = meshFilter.sharedMesh;
        vertices = mesh.vertices;
        int[] triangleVertices = mesh.triangles;

        for (int i = 0; i < triangleVertices.Length; i += 3)
        {
            // Save triangles of mesh filter
            Vector3 v0 = transform.TransformPoint(vertices[triangleVertices[i]]);
            Vector3 v1 = transform.TransformPoint(vertices[triangleVertices[i + 1]]);
            Vector3 v2 = transform.TransformPoint(vertices[triangleVertices[i + 2]]);

            triangles.Add(new Triangle(v0, v1, v2));

            // Calculate surface area of triangle for more uniform point sampling
            Vector3 v01 = v1 - v0;
            Vector3 v02 = v2 - v0;

            triangleSurfaceAreas.Add(Vector3.Magnitude(Vector3.Cross(v01, v02)) / 2);
        }
    }
    
    private void DetermineOutsideCameraFrustum()
    {
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        isOutsideCameraView = !GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
    }


    void CalculateRoughVisibility()
    {
        if (renderer == null)
        {
            renderer = GetComponent<Renderer>();
        }
        
        var bounds = renderer.bounds;
        Vector3 boundPoint0 = bounds.min;
        Vector3 boundPoint1 = bounds.max;
        Vector3 boundPoint2 = new Vector3(boundPoint0.x, boundPoint0.y, boundPoint1.z);
        Vector3 boundPoint3 = new Vector3(boundPoint0.x, boundPoint1.y, boundPoint0.z);
        Vector3 boundPoint4 = new Vector3(boundPoint1.x, boundPoint0.y, boundPoint0.z);
        Vector3 boundPoint5 = new Vector3(boundPoint0.x, boundPoint1.y, boundPoint1.z);
        Vector3 boundPoint6 = new Vector3(boundPoint1.x, boundPoint0.y, boundPoint1.z);
        Vector3 boundPoint7 = new Vector3(boundPoint1.x, boundPoint1.y, boundPoint0.z);

        Vector3[] boundPoints =
            { boundPoint0, boundPoint1, boundPoint2, boundPoint3, boundPoint4, boundPoint5, boundPoint6, boundPoint7 };

        foreach (Vector3 boundPoint in boundPoints)
        {
            if (!Physics.Raycast(boundPoint, cameraPosition - boundPoint,
                    Vector3.Magnitude(cameraPosition - boundPoint)))
            {
                isVisible = true;
            }
        }
    }
    
    void CalculateVisibleTriangles()
    {
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        Vector3 cameraPosition = mainCamera.transform.position;
        Triangle triangle;

        for (int i = 0; i < triangles.Count; i++)
        {
            triangle = triangles[i];
            
            Vector3 v0 = triangle.v0;
            Vector3 v1 = triangle.v1;
            Vector3 v2 = triangle.v2;

            // Check if the triangle is within the camera's view frustum
            if (GeometryUtility.TestPlanesAABB(frustumPlanes, new Bounds((v0 + v1 + v2) / 3, Vector3.one)))
            {
                // Perform backface culling
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                if (Vector3.Dot(normal, (v0 - cameraPosition).normalized) < 0)
                {
                    visibleTriangles.Add(triangle);
                    visibleSurfaceAreas.Add(triangleSurfaceAreas[i]);
                }
                else
                {
                    visibleTriangles.Add(new Triangle(true));
                }
            }
            else
            {
                visibleTriangles.Add(new Triangle(true));
            }
        }
    }

    Triangle PickRandomVisibleTriangle()
    {
        float totalVisibleSurfaceArea = triangleSurfaceAreas.Sum();
        int numberOfTriangles = visibleTriangles.Count;

        float rng = UnityEngine.Random.Range(0f, totalVisibleSurfaceArea);
        int chosenIndex = (int) Math.Round(Remap(rng, 0, totalVisibleSurfaceArea, 0, numberOfTriangles - 1));
        return visibleTriangles[chosenIndex];
    }
    
    public static float Remap (float value, float minRangeFrom, float maxRangeFrom, float minRangeTo, float maxRangeTo) {
        return (value - minRangeFrom) / (maxRangeFrom - minRangeFrom) * (maxRangeTo - minRangeTo) + minRangeTo;
    }

    void CollectUniformSampledSurfacePoints()
    {
        for (int i = 0; i < precision; i++)
        {
            
            Triangle triangle = PickRandomVisibleTriangle();

            while (triangle.isNull)
            {
                triangle = PickRandomVisibleTriangle();
            }
            
            Vector3 v0 = triangle.v0;
            Vector3 v1 = triangle.v1;
            Vector3 v2 = triangle.v2;
            
            Vector3 v01 = v1 - v0;
            Vector3 v02 = v2 - v0;

            float r1 = UnityEngine.Random.Range(0f, 1f);
            float r2 = UnityEngine.Random.Range(0f, 1f);

            if (r1 + r2 > 1)
            {
                r1 = 1 - r1;
                r2 = 1 - r2;
            }

            Vector3 randomSurfacePoint = v0 + r1 * v01 + r2 * v02;
            surfacePoints.Add(randomSurfacePoint);
        }
    }

    void PerformRaycasts()
    {
        float hitCount = 0;

        foreach (Vector3 surfacePoint in surfacePoints)
        {
            RaycastHit[] hits;
            hits = Physics.RaycastAll(surfacePoint, cameraPosition - surfacePoint,
                Vector3.Magnitude(cameraPosition - surfacePoint));
            
            if (drawRays)
            {
                Debug.DrawRay(surfacePoint, cameraPosition - surfacePoint, Color.green);
            }
            
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                Transform hitTransform = hit.transform;

                if (hitTransform != transform && hitTransform != mainCamera.transform)
                {
                    hitCount += 1;
                    break;
                }
            }
        }
        
        visibilityPercentage = (precision - hitCount) / precision;
    }
}

struct Triangle
{
    public Vector3 v0;
    public Vector3 v1;
    public Vector3 v2;

    public bool isNull;

    public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        this.v0 = v0;
        this.v1 = v1;
        this.v2 = v2;
        isNull = false;
    }

    public Triangle(bool isNull)
    {
        v0 = Vector3.zero;
        v1 = Vector3.zero;
        v2 = Vector3.zero;
        this.isNull = isNull;
    }
}
