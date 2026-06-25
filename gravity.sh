#!/bin/sh
# Wrapper installed as /app/bin/Gravity. The real self-contained binary lives in
# /app/lib/Gravity alongside its managed DLLs and native libs; launch it from there
# so the runtime resolves them via its own base directory.
exec /app/lib/Gravity/Gravity "$@"
