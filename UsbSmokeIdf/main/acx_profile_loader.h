#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "acx_profile_layout.h"
#include "profile_store.h"

#ifdef __cplusplus
extern "C" {
#endif

bool app_acx_profile_load_candidate(const char *file_path,
                                    HeadRuntimeProfile *candidate,
                                    AcxProfileLoadResult *result);

bool app_acx_profile_load_candidate_from_bytes(const uint8_t *bytes,
                                               size_t size,
                                               const char *logical_name,
                                               HeadRuntimeProfile *candidate,
                                               AcxProfileLoadResult *result);

#ifdef __cplusplus
}
#endif
