#include "../include/perfectcomms_audio.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

static ma_context g_context;
static ma_device g_device;
static int g_context_ready = 0;
static int g_device_ready = 0;
static char g_last_error[512];
static pc_capture_callback g_capture_callback = NULL;
static pc_playback_callback g_playback_callback = NULL;
static void* g_user_data = NULL;
static uint64_t g_capture_callbacks = 0;
static uint64_t g_playback_callbacks = 0;
static uint64_t g_capture_frames = 0;
static uint64_t g_playback_frames = 0;
static uint64_t g_underruns = 0;

static void set_error(const char* message)
{
    if (message == NULL) message = "";
    snprintf(g_last_error, sizeof(g_last_error), "%s", message);
}

static int copy_string(const char* value, char* buffer, int buffer_len)
{
    if (buffer == NULL || buffer_len <= 0) return 0;
    if (value == NULL) value = "";
    int needed = (int)strlen(value);
    int count = needed < buffer_len - 1 ? needed : buffer_len - 1;
    if (count > 0) memcpy(buffer, value, (size_t)count);
    buffer[count] = '\0';
    return count + 1;
}

static void id_to_hex(const ma_device_id* id, char* buffer, int buffer_len)
{
    static const char* hex = "0123456789abcdef";
    const unsigned char* raw = (const unsigned char*)id;
    int max_bytes = (buffer_len - 1) / 2;
    int bytes = (int)sizeof(ma_device_id);
    if (max_bytes < bytes) bytes = max_bytes;
    for (int i = 0; i < bytes; i++)
    {
        buffer[i * 2] = hex[(raw[i] >> 4) & 0xF];
        buffer[i * 2 + 1] = hex[raw[i] & 0xF];
    }
    buffer[bytes * 2] = '\0';
}

static int hex_value(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}

static int hex_to_id(const char* text, ma_device_id* id)
{
    if (text == NULL || text[0] == '\0') return 0;
    size_t len = strlen(text);
    if (len < sizeof(ma_device_id) * 2) return 0;
    unsigned char* raw = (unsigned char*)id;
    for (size_t i = 0; i < sizeof(ma_device_id); i++)
    {
        int hi = hex_value(text[i * 2]);
        int lo = hex_value(text[i * 2 + 1]);
        if (hi < 0 || lo < 0) return 0;
        raw[i] = (unsigned char)((hi << 4) | lo);
    }
    return 1;
}

static pc_audio_result get_devices(ma_device_info** playback_infos, ma_uint32* playback_count, ma_device_info** capture_infos, ma_uint32* capture_count)
{
    pc_audio_result init = pc_audio_init();
    if (init != PC_AUDIO_OK) return init;
    ma_result result = ma_context_get_devices(&g_context, playback_infos, playback_count, capture_infos, capture_count);
    if (result != MA_SUCCESS)
    {
        set_error("ma_context_get_devices failed");
        return PC_AUDIO_ERROR_NATIVE_INIT;
    }
    return PC_AUDIO_OK;
}

pc_audio_result pc_audio_init(void)
{
    if (g_context_ready) return PC_AUDIO_OK;
    ma_result result = ma_context_init(NULL, 0, NULL, &g_context);
    if (result != MA_SUCCESS)
    {
        set_error("ma_context_init failed");
        return PC_AUDIO_ERROR_NATIVE_INIT;
    }
    g_context_ready = 1;
    set_error("");
    return PC_AUDIO_OK;
}

void pc_audio_shutdown(void)
{
    pc_audio_stop_full_duplex();
    if (g_context_ready)
    {
        ma_context_uninit(&g_context);
        g_context_ready = 0;
    }
}

int pc_audio_get_input_count(void)
{
    ma_device_info* playback_infos = NULL;
    ma_device_info* capture_infos = NULL;
    ma_uint32 playback_count = 0;
    ma_uint32 capture_count = 0;
    if (get_devices(&playback_infos, &playback_count, &capture_infos, &capture_count) != PC_AUDIO_OK) return 0;
    return (int)capture_count;
}

int pc_audio_get_output_count(void)
{
    ma_device_info* playback_infos = NULL;
    ma_device_info* capture_infos = NULL;
    ma_uint32 playback_count = 0;
    ma_uint32 capture_count = 0;
    if (get_devices(&playback_infos, &playback_count, &capture_infos, &capture_count) != PC_AUDIO_OK) return 0;
    return (int)playback_count;
}

int pc_audio_get_input_name(int index, char* buffer, int buffer_len)
{
    ma_device_info* playback_infos = NULL;
    ma_device_info* capture_infos = NULL;
    ma_uint32 playback_count = 0;
    ma_uint32 capture_count = 0;
    if (get_devices(&playback_infos, &playback_count, &capture_infos, &capture_count) != PC_AUDIO_OK) return 0;
    if (index < 0 || (ma_uint32)index >= capture_count) return copy_string("", buffer, buffer_len);
    return copy_string(capture_infos[index].name, buffer, buffer_len);
}

int pc_audio_get_output_name(int index, char* buffer, int buffer_len)
{
    ma_device_info* playback_infos = NULL;
    ma_device_info* capture_infos = NULL;
    ma_uint32 playback_count = 0;
    ma_uint32 capture_count = 0;
    if (get_devices(&playback_infos, &playback_count, &capture_infos, &capture_count) != PC_AUDIO_OK) return 0;
    if (index < 0 || (ma_uint32)index >= playback_count) return copy_string("", buffer, buffer_len);
    return copy_string(playback_infos[index].name, buffer, buffer_len);
}

int pc_audio_get_input_id(int index, char* buffer, int buffer_len)
{
    ma_device_info* playback_infos = NULL;
    ma_device_info* capture_infos = NULL;
    ma_uint32 playback_count = 0;
    ma_uint32 capture_count = 0;
    if (get_devices(&playback_infos, &playback_count, &capture_infos, &capture_count) != PC_AUDIO_OK) return 0;
    if (index < 0 || (ma_uint32)index >= capture_count) return copy_string("", buffer, buffer_len);
    char id[sizeof(ma_device_id) * 2 + 1];
    id_to_hex(&capture_infos[index].id, id, (int)sizeof(id));
    return copy_string(id, buffer, buffer_len);
}

int pc_audio_get_output_id(int index, char* buffer, int buffer_len)
{
    ma_device_info* playback_infos = NULL;
    ma_device_info* capture_infos = NULL;
    ma_uint32 playback_count = 0;
    ma_uint32 capture_count = 0;
    if (get_devices(&playback_infos, &playback_count, &capture_infos, &capture_count) != PC_AUDIO_OK) return 0;
    if (index < 0 || (ma_uint32)index >= playback_count) return copy_string("", buffer, buffer_len);
    char id[sizeof(ma_device_id) * 2 + 1];
    id_to_hex(&playback_infos[index].id, id, (int)sizeof(id));
    return copy_string(id, buffer, buffer_len);
}

static void data_callback(ma_device* device, void* output, const void* input, ma_uint32 frame_count)
{
    (void)device;
    if (input != NULL && g_capture_callback != NULL)
    {
        g_capture_callbacks++;
        g_capture_frames += frame_count;
        g_capture_callback((const float*)input, (int)frame_count, g_device.capture.channels, g_user_data);
    }

    if (output != NULL)
    {
        g_playback_callbacks++;
        g_playback_frames += frame_count;
        int written = 0;
        if (g_playback_callback != NULL)
            written = g_playback_callback((float*)output, (int)frame_count, g_device.playback.channels, g_user_data);
        if (written <= 0)
        {
            memset(output, 0, frame_count * g_device.playback.channels * sizeof(float));
            g_underruns++;
        }
    }
}

pc_audio_result pc_audio_start_full_duplex(
    const char* input_id,
    const char* output_id,
    int sample_rate,
    int input_channels,
    int output_channels,
    pc_capture_callback capture_callback,
    pc_playback_callback playback_callback,
    void* user_data)
{
    pc_audio_result init = pc_audio_init();
    if (init != PC_AUDIO_OK) return init;
    pc_audio_stop_full_duplex();

    ma_device_id capture_id;
    ma_device_id playback_id;
    ma_device_id* capture_id_ptr = NULL;
    ma_device_id* playback_id_ptr = NULL;
    if (input_id != NULL && input_id[0] != '\0')
    {
        if (!hex_to_id(input_id, &capture_id))
        {
            set_error("selected input id is invalid");
            return PC_AUDIO_ERROR_INPUT_NOT_FOUND;
        }
        capture_id_ptr = &capture_id;
    }
    if (output_id != NULL && output_id[0] != '\0')
    {
        if (!hex_to_id(output_id, &playback_id))
        {
            set_error("selected output id is invalid");
            return PC_AUDIO_ERROR_OUTPUT_NOT_FOUND;
        }
        playback_id_ptr = &playback_id;
    }

    ma_device_config config = ma_device_config_init(ma_device_type_duplex);
    config.capture.pDeviceID = capture_id_ptr;
    config.capture.format = ma_format_f32;
    config.capture.channels = (ma_uint32)input_channels;
    config.playback.pDeviceID = playback_id_ptr;
    config.playback.format = ma_format_f32;
    config.playback.channels = (ma_uint32)output_channels;
    config.sampleRate = (ma_uint32)sample_rate;
    config.dataCallback = data_callback;

    g_capture_callback = capture_callback;
    g_playback_callback = playback_callback;
    g_user_data = user_data;

    ma_result result = ma_device_init(&g_context, &config, &g_device);
    if (result != MA_SUCCESS)
    {
        set_error("ma_device_init duplex failed");
        g_capture_callback = NULL;
        g_playback_callback = NULL;
        g_user_data = NULL;
        return PC_AUDIO_ERROR_NATIVE_INIT;
    }

    g_device_ready = 1;
    result = ma_device_start(&g_device);
    if (result != MA_SUCCESS)
    {
        set_error("ma_device_start duplex failed");
        pc_audio_stop_full_duplex();
        return PC_AUDIO_ERROR_OUTPUT_START_FAILED;
    }

    set_error("");
    return PC_AUDIO_OK;
}

void pc_audio_stop_full_duplex(void)
{
    if (g_device_ready)
    {
        ma_device_stop(&g_device);
        ma_device_uninit(&g_device);
        g_device_ready = 0;
    }
    g_capture_callback = NULL;
    g_playback_callback = NULL;
    g_user_data = NULL;
}

int pc_audio_get_last_error(char* buffer, int buffer_len)
{
    return copy_string(g_last_error, buffer, buffer_len);
}

int pc_audio_get_stats(char* json_buffer, int buffer_len)
{
    char stats[512];
    snprintf(stats, sizeof(stats),
        "{\"captureCallbacks\":%llu,\"playbackCallbacks\":%llu,\"captureFrames\":%llu,\"playbackFrames\":%llu,\"underruns\":%llu,\"deviceReady\":%s}",
        (unsigned long long)g_capture_callbacks,
        (unsigned long long)g_playback_callbacks,
        (unsigned long long)g_capture_frames,
        (unsigned long long)g_playback_frames,
        (unsigned long long)g_underruns,
        g_device_ready ? "true" : "false");
    return copy_string(stats, json_buffer, buffer_len);
}
