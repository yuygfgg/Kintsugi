cmake_minimum_required(VERSION 3.24)

project(KintsugiPluginHost LANGUAGES C CXX)

include(FetchContent)

if(APPLE)
  enable_language(OBJC)
  enable_language(OBJCXX)
  set(CMAKE_OSX_DEPLOYMENT_TARGET 12.0 CACHE STRING "" FORCE)
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

set(KINTSUGI_DEPENDENCY_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../.deps" CACHE PATH
    "Directory used for downloaded third-party source dependencies")
file(MAKE_DIRECTORY "${KINTSUGI_DEPENDENCY_DIR}")

set(FETCHCONTENT_BASE_DIR "${KINTSUGI_DEPENDENCY_DIR}" CACHE PATH
    "FetchContent base directory" FORCE)
set(FETCHCONTENT_UPDATES_DISCONNECTED_JUCE ON CACHE BOOL
    "Do not update the cached JUCE checkout automatically during normal builds" FORCE)

FetchContent_Declare(juce
    GIT_REPOSITORY https://github.com/juce-framework/JUCE.git
    GIT_TAG 8.0.12
    GIT_SHALLOW TRUE
    GIT_PROGRESS TRUE
)

FetchContent_MakeAvailable(juce)

add_library(KintsugiPluginHost SHARED
    PluginHost.cpp
)

target_compile_definitions(KintsugiPluginHost
    PRIVATE
        JUCE_APPLICATION_NAME_STRING="Kintsugi Plugin Host"
        JUCE_APPLICATION_VERSION_STRING="1.0.0"
        JUCE_STANDALONE_APPLICATION=0
        JUCE_PLUGINHOST_VST=0
        JUCE_PLUGINHOST_VST3=1
        JUCE_PLUGINHOST_AU=$<IF:$<PLATFORM_ID:Darwin>,1,0>
        JUCE_PLUGINHOST_LADSPA=0
        JUCE_PLUGINHOST_LV2=0
        JUCE_USE_CURL=0
        JUCE_WEB_BROWSER=0
)

target_link_libraries(KintsugiPluginHost
    PRIVATE
        juce::juce_audio_processors
        juce::juce_gui_basics
        juce::juce_recommended_config_flags
        juce::juce_recommended_warning_flags
)

if(CMAKE_CXX_COMPILER_ID MATCHES "Clang")
  set_source_files_properties(
      PluginHost.cpp
      PROPERTIES COMPILE_OPTIONS "-Wno-shadow-field-in-constructor"
  )
  set_source_files_properties(
      "${juce_SOURCE_DIR}/modules/juce_audio_processors_headless/juce_audio_processors_headless.mm"
      PROPERTIES COMPILE_OPTIONS "-Wno-deprecated-declarations"
  )
endif()

if(APPLE)
  target_link_options(KintsugiPluginHost PRIVATE "-Wl,-rpath,@loader_path")
  target_link_libraries(KintsugiPluginHost PRIVATE "-framework CoreAudioKit")
endif()
