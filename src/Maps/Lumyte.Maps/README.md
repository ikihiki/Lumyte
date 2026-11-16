# Lumyte.Maps

CPU-only XYZ identifiers, double-precision Web Mercator coordinates and immutable MVT geometry.
World width is 512 logical pixels at zoom zero. X wraps; Y does not. Each layer retains its own extent,
source ordering, typed attributes, buffered coordinates and polygon ring winding.

Local MVT source/pipeline usage is documented in ../Lumyte.Maps.Mvt/README.md.
This package does not interpret OSM XML or .osm.pbf, select arbitrary online sources, or promise
external style compatibility. The supported application schema is explicit.
