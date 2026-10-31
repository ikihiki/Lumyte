include_guard(GLOBAL)

# Backport the build workaround from microsoft/DirectXShaderCompiler#8176.
# GCC 13's loop unswitching miscompiles Release DXC (GCC PR109934).
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU" AND
   CMAKE_CXX_COMPILER_VERSION VERSION_GREATER_EQUAL 13.0 AND
   CMAKE_CXX_COMPILER_VERSION VERSION_LESS 14.0)
    add_compile_options(-fno-unswitch-loops)
endif()
