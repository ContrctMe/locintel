# The one PostgreSQL image for the shell scripts (ADR 50). Must equal
# PostgresImage.Reference in src/LocIntel.Platform/Data/PostgresImage.cs -
# PostgresImageTests (architecture tests) fails the build if it drifts.
# shellcheck disable=SC2034
LOCINTEL_POSTGRES_IMAGE="imresamu/postgis:17-3.5-alpine@sha256:2b52785e156e5fe881c0841a8032694bbdf22a46e42a3e26aac6eebb03ab182c"
