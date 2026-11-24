using UnityEngine;
using UnityEngine.SceneManagement;

namespace MindScape.Environment
{
    public static class AmbienceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Install;
            SceneManager.sceneLoaded += Install;
        }
        static void Install(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "BasicScene" || Object.FindObjectOfType<AmbienceController>() != null) return;
            var room = GameObject.Find("Room");
            if (room == null) return;
            var root = new GameObject("Mode B Ambience");
            SceneManager.MoveGameObjectToScene(root, scene);
            var controller = root.AddComponent<AmbienceController>();
            // Deliberately scope to room lights plus the named scene sun, excluding NPC eye effects.
            var targets = new System.Collections.Generic.List<Light>(room.GetComponentsInChildren<Light>());
            var sun = GameObject.Find("Directional Light");
            if (sun != null && sun.TryGetComponent<Light>(out var light) && !targets.Contains(light)) targets.Add(light);
            controller.lights = targets.ToArray();
            controller.furniture = CollectFurniture(room);
            controller.walls = CollectWalls(room);
            controller.floors = CollectFloors(room);
            controller.lamps = CollectLamps(room);
            controller.pillows = CollectPillows(room);
            controller.plants = CollectPlants(room);
            controller.plantReplacements = InstantiateTulips(controller.plants);
            controller.coldWhiteLights = CollectColdLights(room);
            controller.screens = CollectAtPath2("Room/ConversationScreen/Display");
            controller.screenFrames = CollectAtPath2("Room/ConversationScreen/Frame");
            controller.cabinetGlows = CollectCabinetGlows(room);
            controller.sadnessIntroSound = Resources.Load<AudioClip>("MindScapeAmbience/SadPiano");
            controller.sadnessSound = Resources.Load<AudioClip>("MindScapeAmbience/SoftRain");
            controller.angerSound = Resources.Load<AudioClip>("MindScapeAmbience/QuietStream");
            controller.calmSound = Resources.Load<AudioClip>("MindScapeAmbience/GentleAir");
            controller.AdjustRoomAmbient = true;
            controller.Initialize();
            root.AddComponent<AmbienceCommandBridge>().Bind(controller);
            root.AddComponent<AmbiencePanel>().Bind(controller);
        }
        // Furniture tinting scope: seating and case goods only. The sand tray,
        // collected sand pieces, plants and lamps deliberately stay untouched.
        // Throw pillows are carved out into their own group (see CollectPillows).
        static Renderer[] CollectFurniture(GameObject room)
        {
            var pillows = new System.Collections.Generic.HashSet<Renderer>(CollectPillows(room));
            var furniture = new System.Collections.Generic.List<Renderer>();
            Collect(room.transform, "Sofa", furniture);
            Collect(room.transform, "Armchair", furniture);
            CollectAtPath("Room/RoomShell/Oak side table", furniture);
            CollectAtPath("Room/Daylit room details/Reading corner", furniture);
            CollectAtPath("Room/Daylit room details/Knitted lap throw", furniture);
            furniture.RemoveAll(pillows.Contains);
            return furniture.ToArray();
        }
        // The window-side floor lamp: it runs cold white through both the sadness and
        // anger presets while the rest of the room follows each preset's kelvin.
        static Light[] CollectColdLights(GameObject room)
        {
            var lamp = room.transform.Find("FloorLamp");
            return lamp == null ? new Light[0] : lamp.GetComponentsInChildren<Light>(false);
        }
        // The conversation screen: untextured Porcelain display + Sunflower frame.
        static Renderer[] CollectAtPath2(string path)
        {
            var found = GameObject.Find(path);
            return found == null ? new Renderer[0] : found.GetComponentsInChildren<Renderer>(false);
        }
        // The warm diffuser strips inside every display cabinet: they stay warm through
        // sadness and cool to cold white when angry.
        static Renderer[] CollectCabinetGlows(GameObject room)
        {
            var glows = new System.Collections.Generic.List<Renderer>();
            CollectCabinetGlowsFrom(room.transform, "DisplayShelf/Oak cabinet and display lights", glows);
            var shelves = room.transform.Find("CollectionShelves");
            if (shelves != null)
                foreach (Transform category in shelves)
                    CollectCabinetGlowsFrom(category, "Oak cabinet and display lights", glows);
            return glows.ToArray();
        }
        static void CollectCabinetGlowsFrom(Transform parent, string childName, System.Collections.Generic.List<Renderer> sink)
        {
            var cabinets = parent.Find(childName);
            if (cabinets == null) return;
            foreach (Transform child in cabinets)
                if (child.name == "Warm diffuser")
                    sink.AddRange(child.GetComponentsInChildren<Renderer>(false));
        }
        // Throw pillows: the square linen cushions on the sofa and armchair. They get
        // their own white / light-gray palette instead of the furniture blue-white/gray.
        static Renderer[] CollectPillows(GameObject room)
        {
            var pillows = new System.Collections.Generic.List<Renderer>();
            CollectPillowsOn(room.transform, "Sofa", pillows);
            CollectPillowsOn(room.transform, "Armchair", pillows);
            return pillows.ToArray();
        }
        static void CollectPillowsOn(Transform parent, string seatName, System.Collections.Generic.List<Renderer> sink)
        {
            var soft = parent.Find(seatName + "/Soft furnishings");
            if (soft == null) return;
            foreach (Transform child in soft)
                if (child.name == "Square linen cushion")
                    sink.AddRange(child.GetComponentsInChildren<Renderer>(false));
        }
        // Wall planes of the shell: the named walls, the entry jambs, the lintel, the ceiling
        // and the plaster around the west window. Ceiling wash averages the same warm cream
        // as Limewash, so the compensated wall multipliers apply to both. Skirting and window
        // frames are untextured Trim and would show the compensated multiplier directly as
        // blue, so they stay out. Window glass, the outside view, curtains, rug, wall art
        // and plants also deliberately stay untouched.
        static Renderer[] CollectWalls(GameObject room)
        {
            var walls = new System.Collections.Generic.List<Renderer>();
            var shell = room.transform.Find("RoomShell");
            if (shell != null)
                foreach (Transform child in shell)
                    if (child.name == "Feature wall" || child.name == "West wall" || child.name == "East wall"
                        || child.name == "Entry left" || child.name == "Entry right"
                        || child.name == "Lintel" || child.name == "Cream ceiling")
                        walls.AddRange(child.GetComponentsInChildren<Renderer>(false));
            var window = room.transform.Find("Daylit room details/West window and linen voile");
            if (window != null)
                foreach (Transform child in window)
                    if (child.name == "Sill wall" || child.name == "Head wall" || child.name == "Side of opening")
                        walls.AddRange(child.GetComponentsInChildren<Renderer>(false));
            return walls.ToArray();
        }
        static Renderer[] CollectFloors(GameObject room)
        {
            var floors = new System.Collections.Generic.List<Renderer>();
            Collect(room.transform, "RoomShell/Floor", floors);
            return floors.ToArray();
        }
        // The whole pendant fixture: cane shade, glowing opal glass and walnut hardware.
        // Its Light components are already covered by the lights channel (kelvin/brightness).
        static Renderer[] CollectLamps(GameObject room)
        {
            var lamps = new System.Collections.Generic.List<Renderer>();
            Collect(room.transform, "RoomShell/Woven pendant", lamps);
            return lamps.ToArray();
        }
        // Only the plant by the mirror-like conversation screen swaps for tulips during
        // sadness. The window-side plants (palm + monstera cluster) stay green, and the
        // sand-table pine stays put.
        static GameObject[] CollectPlants(GameObject room)
        {
            var plants = new System.Collections.Generic.List<GameObject>();
            AddIfExists(room.transform, "RoomShell/Corner foliage", plants);
            return plants.ToArray();
        }
        static void AddIfExists(Transform parent, string path, System.Collections.Generic.List<GameObject> sink)
        {
            var found = parent.Find(path);
            if (found != null) sink.Add(found.gameObject);
        }
        // One tulip per plant cluster (plants sharing a floor position form one cluster),
        // instantiated from Resources at the plant's spot, hidden until sadness.
        static GameObject[] InstantiateTulips(GameObject[] plants)
        {
            var tulips = new System.Collections.Generic.List<GameObject>();
            var prefab = Resources.Load<GameObject>("MindScapeEnvironment/PottedTulip");
            if (prefab == null || plants == null) return tulips.ToArray();
            var clusters = new System.Collections.Generic.HashSet<Vector2>();
            foreach (var plant in plants)
            {
                if (plant == null) continue;
                var position = plant.transform.position;
                var cluster = new Vector2(Mathf.Round(position.x * 4f) / 4f, Mathf.Round(position.z * 4f) / 4f);
                if (!clusters.Add(cluster)) continue;
                var tulip = Object.Instantiate(prefab, position, plant.transform.rotation);
                tulip.name = "Potted Tulip";
                tulip.transform.localScale = Vector3.one * 1.1f; // ~0.9 m tall floor plant
                tulip.SetActive(false);
                tulips.Add(tulip);
            }
            return tulips.ToArray();
        }
        static void Collect(Transform parent, string childName, System.Collections.Generic.List<Renderer> sink)
        {
            var child = parent.Find(childName);
            if (child != null) sink.AddRange(child.GetComponentsInChildren<Renderer>(false));
        }
        static void CollectAtPath(string path, System.Collections.Generic.List<Renderer> sink)
        {
            var found = GameObject.Find(path);
            if (found != null) sink.AddRange(found.GetComponentsInChildren<Renderer>(false));
        }
    }
}
