// Leaflet + OpenStreetMap interop shared by the StreetFlow Blazor WASM surfaces.
// Kept deliberately tiny: init a map, mirror .NET state (one polygon + labeled
// markers) onto it, report clicks back to .NET, copy text to the clipboard.
window.streetflowMap = (function () {
    const instances = {};

    function get(id) {
        const inst = instances[id];
        if (!inst) throw new Error("Map not initialized: " + id);
        return inst;
    }

    return {
        init: function (elementId, centerLat, centerLng, zoom, dotnetRef) {
            if (instances[elementId]) {
                instances[elementId].map.remove();
                delete instances[elementId];
            }
            const map = L.map(elementId).setView([centerLat, centerLng], zoom);
            L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
                maxZoom: 19,
                attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            }).addTo(map);
            instances[elementId] = { map: map, polygon: null, markers: [] };
            if (dotnetRef) {
                map.on("click", function (e) {
                    dotnetRef.invokeMethodAsync("OnMapClicked", e.latlng.lat, e.latlng.lng);
                });
            }
        },

        // latLngs: [[lat, lng], ...]
        setPolygon: function (elementId, latLngs) {
            const inst = get(elementId);
            if (inst.polygon) {
                inst.map.removeLayer(inst.polygon);
                inst.polygon = null;
            }
            if (latLngs && latLngs.length >= 2) {
                inst.polygon = L.polygon(latLngs, { color: "#2e86de", weight: 2, fillOpacity: 0.12 }).addTo(inst.map);
            }
        },

        // points: [{ lat, lng, label }]
        setPoints: function (elementId, points) {
            const inst = get(elementId);
            inst.markers.forEach(function (m) { inst.map.removeLayer(m); });
            inst.markers = [];
            (points || []).forEach(function (p) {
                const marker = L.marker([p.lat, p.lng]).addTo(inst.map);
                if (p.label) {
                    marker.bindTooltip(p.label, { permanent: true, direction: "top", offset: [-14, -12] });
                }
                inst.markers.push(marker);
            });
        },

        fit: function (elementId) {
            const inst = get(elementId);
            const layers = inst.markers.slice();
            if (inst.polygon) layers.push(inst.polygon);
            if (layers.length === 0) return;
            inst.map.fitBounds(L.featureGroup(layers).getBounds().pad(0.3));
        },

        copyText: function (text) {
            return navigator.clipboard.writeText(text);
        }
    };
})();
