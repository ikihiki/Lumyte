namespace Lumyte.Graphics.Vulkan.Samples;

internal static class SampleShaders
{
    internal const string TriangleVertex = """
        #version 450
        const vec2 positions[3] = vec2[3](
            vec2(-0.78, -0.72),
            vec2( 0.78, -0.72),
            vec2( 0.00,  0.78));
        const vec3 colors[3] = vec3[3](
            vec3(1.0, 0.08, 0.18),
            vec3(0.05, 0.85, 0.35),
            vec3(0.12, 0.35, 1.0));
        layout(location = 0) out vec3 vertexColor;
        void main()
        {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            vertexColor = colors[gl_VertexIndex];
        }
        """;

    internal const string TrianglePixel = """
        #version 450
        layout(location = 0) in vec3 vertexColor;
        layout(location = 0) out vec4 color;
        void main() { color = vec4(vertexColor, 1.0); }
        """;

    internal const string QuadVertex = """
        #version 450
        const vec2 positions[6] = vec2[6](
            vec2(-0.78, -0.78), vec2( 0.78, -0.78), vec2( 0.78,  0.78),
            vec2(-0.78, -0.78), vec2( 0.78,  0.78), vec2(-0.78,  0.78));
        const vec2 uvs[6] = vec2[6](
            vec2(0.0, 0.0), vec2(1.0, 0.0), vec2(1.0, 1.0),
            vec2(0.0, 0.0), vec2(1.0, 1.0), vec2(0.0, 1.0));
        layout(location = 0) out vec2 uv;
        void main()
        {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            uv = uvs[gl_VertexIndex];
        }
        """;

    internal const string TexturedPixel = """
        #version 450
        layout(set = 0, binding = 0) uniform texture2D textures[64];
        layout(set = 1, binding = 0) uniform sampler samplers[64];
        layout(push_constant) uniform RootData { uint textureIndex; uint samplerIndex; } rootData;
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        void main()
        {
            color = texture(
                sampler2D(textures[rootData.textureIndex], samplers[rootData.samplerIndex]),
                uv);
        }
        """;

    internal const string FullscreenVertex = """
        #version 450
        const vec2 positions[6] = vec2[6](
            vec2(-1.0, -1.0), vec2( 1.0, -1.0), vec2( 1.0,  1.0),
            vec2(-1.0, -1.0), vec2( 1.0,  1.0), vec2(-1.0,  1.0));
        const vec2 uvs[6] = vec2[6](
            vec2(0.0, 0.0), vec2(1.0, 0.0), vec2(1.0, 1.0),
            vec2(0.0, 0.0), vec2(1.0, 1.0), vec2(0.0, 1.0));
        layout(location = 0) out vec2 uv;
        void main()
        {
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            uv = uvs[gl_VertexIndex];
        }
        """;

    internal const string ScenePixel = """
        #version 450
        layout(push_constant) uniform RootData
        {
            float time;
            float aspect;
            vec2 padding;
        } rootData;
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;

        mat3 rotationX(float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);
            return mat3(
                1.0, 0.0, 0.0,
                0.0, cosine, sine,
                0.0, -sine, cosine);
        }

        mat3 rotationY(float angle)
        {
            float sine = sin(angle);
            float cosine = cos(angle);
            return mat3(
                cosine, 0.0, -sine,
                0.0, 1.0, 0.0,
                sine, 0.0, cosine);
        }

        bool intersectCube(
            vec3 rayOrigin,
            vec3 rayDirection,
            vec3 halfSize,
            out float nearDistance,
            out float farDistance)
        {
            vec3 inverseDirection = 1.0 / rayDirection;
            vec3 firstHit = (-halfSize - rayOrigin) * inverseDirection;
            vec3 secondHit = (halfSize - rayOrigin) * inverseDirection;
            vec3 nearHit = min(firstHit, secondHit);
            vec3 farHit = max(firstHit, secondHit);
            nearDistance = max(max(nearHit.x, nearHit.y), nearHit.z);
            farDistance = min(min(farHit.x, farHit.y), farHit.z);
            return nearDistance <= farDistance && farDistance >= 0.0;
        }

        float floorVisibility(
            vec3 floorPosition,
            vec3 lightPosition,
            mat3 inverseCubeRotation,
            vec3 halfSize)
        {
            const vec3 lightOffsets[5] = vec3[5](
                vec3(0.0, 0.0, 0.0),
                vec3(0.10, 0.0, 0.0),
                vec3(-0.10, 0.0, 0.0),
                vec3(0.0, 0.0, 0.10),
                vec3(0.0, 0.0, -0.10));
            float visibleSamples = 0.0;
            for (int index = 0; index < 5; index++)
            {
                vec3 sampleVector = lightPosition + lightOffsets[index] - floorPosition;
                float sampleDistance = length(sampleVector);
                vec3 sampleDirection = sampleVector / sampleDistance;
                vec3 localOrigin = inverseCubeRotation
                    * (floorPosition + vec3(0.0, 0.006, 0.0));
                vec3 localDirection = inverseCubeRotation * sampleDirection;
                float nearDistance;
                float farDistance;
                bool blocked = intersectCube(
                    localOrigin,
                    localDirection,
                    halfSize,
                    nearDistance,
                    farDistance)
                    && max(nearDistance, 0.0) < sampleDistance;
                visibleSamples += blocked ? 0.0 : 1.0;
            }
            return 0.18 + 0.82 * visibleSamples / 5.0;
        }

        void main()
        {
            vec2 screen = uv * 2.0 - 1.0;
            screen.y = -screen.y;
            screen.x *= rootData.aspect;
            vec3 rayOrigin = vec3(0.0, 0.08, 3.4);
            vec3 rayDirection = normalize(vec3(screen, -2.2));
            mat3 cubeRotation = rotationY(
                0.62 + sin(rootData.time * 0.45) * 0.24)
                * rotationX(-0.42 + sin(rootData.time * 0.31) * 0.07);
            mat3 inverseCubeRotation = transpose(cubeRotation);
            vec3 localOrigin = inverseCubeRotation * rayOrigin;
            vec3 localDirection = inverseCubeRotation * rayDirection;
            const vec3 halfSize = vec3(0.72);
            float nearDistance;
            float farDistance;
            bool cubeHit = intersectCube(
                localOrigin,
                localDirection,
                halfSize,
                nearDistance,
                farDistance);
            const float floorHeight = -1.02;
            float floorDistance = rayDirection.y < -0.0001
                ? (floorHeight - rayOrigin.y) / rayDirection.y
                : -1.0;
            bool floorHit = floorDistance > 0.0;

            vec3 background = mix(
                vec3(0.008, 0.014, 0.035),
                vec3(0.055, 0.10, 0.17),
                uv.y);
            float vignette = 1.0 - smoothstep(0.45, 1.25, length(screen * vec2(0.55, 0.8)));
            background *= 0.55 + vignette * 0.45;
            if (!cubeHit && !floorHit)
            {
                color = vec4(background, 1.0);
                return;
            }

            vec3 lightPosition = vec3(
                sin(rootData.time * 0.75) * 0.9,
                0.85 + cos(rootData.time * 0.45) * 0.15,
                1.5);
            vec3 lightColor = vec3(1.0, 0.48, 0.14);
            if (floorHit && (!cubeHit || floorDistance < max(nearDistance, 0.0)))
            {
                vec3 floorPosition = rayOrigin + rayDirection * floorDistance;
                vec3 lightVector = lightPosition - floorPosition;
                float lightDistance = length(lightVector);
                float diffuse = max(lightVector.y / lightDistance, 0.0);
                float attenuation = 1.0 / (1.0 + lightDistance * lightDistance * 0.12);
                float visibility = floorVisibility(
                    floorPosition,
                    lightPosition,
                    inverseCubeRotation,
                    halfSize);
                vec2 tile = floor(floorPosition.xz * 1.35);
                float checker = mod(tile.x + tile.y, 2.0);
                vec3 floorMaterial = mix(
                    vec3(0.055, 0.075, 0.105),
                    vec3(0.075, 0.098, 0.13),
                    checker);
                vec3 floorColor = floorMaterial
                    * (0.16 + lightColor * diffuse * attenuation * 2.1 * visibility);
                float horizonFade = 1.0 - smoothstep(2.0, 10.0, length(floorPosition.xz));
                floorColor = mix(background, floorColor, horizonFade);
                color = vec4(floorColor, 1.0);
                return;
            }

            vec3 localPosition = localOrigin + localDirection * max(nearDistance, 0.0);
            vec3 absolutePosition = abs(localPosition);
            vec3 localNormal;
            vec3 faceTint;
            float edgeDistance;
            if (absolutePosition.x > absolutePosition.y
                && absolutePosition.x > absolutePosition.z)
            {
                localNormal = vec3(sign(localPosition.x), 0.0, 0.0);
                faceTint = vec3(0.72, 0.88, 1.16);
                edgeDistance = min(
                    halfSize.y - absolutePosition.y,
                    halfSize.z - absolutePosition.z);
            }
            else if (absolutePosition.y > absolutePosition.z)
            {
                localNormal = vec3(0.0, sign(localPosition.y), 0.0);
                faceTint = vec3(1.12, 1.08, 0.82);
                edgeDistance = min(
                    halfSize.x - absolutePosition.x,
                    halfSize.z - absolutePosition.z);
            }
            else
            {
                localNormal = vec3(0.0, 0.0, sign(localPosition.z));
                faceTint = vec3(0.92, 1.0, 1.08);
                edgeDistance = min(
                    halfSize.x - absolutePosition.x,
                    halfSize.y - absolutePosition.y);
            }

            vec3 worldPosition = cubeRotation * localPosition;
            vec3 worldNormal = normalize(cubeRotation * localNormal);
            vec3 lightVector = lightPosition - worldPosition;
            float lightDistance = length(lightVector);
            vec3 lightDirection = lightVector / lightDistance;
            vec3 viewDirection = normalize(rayOrigin - worldPosition);
            vec3 halfDirection = normalize(lightDirection + viewDirection);
            float diffuse = max(dot(worldNormal, lightDirection), 0.0);
            float specular = pow(max(dot(worldNormal, halfDirection), 0.0), 48.0);
            float attenuation = 1.0 / (1.0 + lightDistance * lightDistance * 0.18);
            vec3 material = vec3(0.10, 0.32, 0.72) * faceTint;
            vec3 litColor = material * (0.16 + lightColor * diffuse * attenuation * 2.5);
            litColor += lightColor * specular * attenuation * 1.4;
            float edge = 1.0 - smoothstep(0.012, 0.035, edgeDistance);
            litColor = mix(litColor, vec3(0.025, 0.045, 0.085), edge * 0.78);
            color = vec4(litColor, 1.0);
        }
        """;

    internal const string LightPixel = """
        #version 450
        layout(push_constant) uniform RootData
        {
            float time;
            float aspect;
            vec2 padding;
        } rootData;
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        void main()
        {
            vec3 cameraPosition = vec3(0.0, 0.08, 3.4);
            vec3 lightPosition = vec3(
                sin(rootData.time * 0.75) * 0.9,
                0.85 + cos(rootData.time * 0.45) * 0.15,
                1.5);
            float projectionScale = (cameraPosition.z - lightPosition.z) / 2.2;
            vec2 center = vec2(
                (lightPosition.x - cameraPosition.x)
                    / (projectionScale * rootData.aspect),
                -(lightPosition.y - cameraPosition.y) / projectionScale);
            center = center * 0.5 + 0.5;
            vec2 offset = uv - center;
            offset.x *= rootData.aspect;
            float distanceFromLight = length(offset);
            float glow = 1.0 - smoothstep(0.025, 0.19, distanceFromLight);
            float core = 1.0 - smoothstep(0.0, 0.028, distanceFromLight);
            vec3 lightColor = vec3(1.0, 0.46, 0.12);
            color = vec4(lightColor * (glow * glow * 0.34 + core * 1.15), 0.0);
        }
        """;

    internal const string MaterialVertex = """
        #version 450
        layout(set = 2, binding = 0, std430) readonly buffer ShaderBuffer
        {
            float values[];
        } shaderBuffers[64];
        layout(push_constant) uniform RootData
        {
            mat4 world;
            mat4 viewProjection;
        } rootData;
        layout(location = 0) out vec2 uv;
        void main()
        {
            uint offset = uint(gl_VertexIndex) * 4u;
            vec2 position = vec2(
                shaderBuffers[0].values[offset],
                shaderBuffers[0].values[offset + 1u]);
            uv = vec2(
                shaderBuffers[0].values[offset + 2u],
                shaderBuffers[0].values[offset + 3u]);
            gl_Position = rootData.viewProjection
                * rootData.world
                * vec4(position, 0.0, 1.0);
        }
        """;

    internal const string MaterialPixel = """
        #version 450
        layout(set = 0, binding = 0) uniform texture2D textures[64];
        layout(set = 1, binding = 0) uniform sampler samplers[64];
        layout(set = 2, binding = 0, std430) readonly buffer ShaderBuffer
        {
            float values[];
        } shaderBuffers[64];
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 color;
        void main()
        {
            vec4 tint = vec4(
                shaderBuffers[1].values[0],
                shaderBuffers[1].values[1],
                shaderBuffers[1].values[2],
                shaderBuffers[1].values[3]);
            vec2 uvScale = vec2(
                shaderBuffers[1].values[4],
                shaderBuffers[1].values[5]);
            vec4 texel = texture(
                sampler2D(textures[0], samplers[0]),
                fract(uv * uvScale));
            color = texel * tint;
        }
        """;
}
