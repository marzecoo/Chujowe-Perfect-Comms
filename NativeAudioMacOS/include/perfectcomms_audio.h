#pragma once

#ifdef __cplusplus
extern "C" {
#endif

typedef enum pc_audio_result {
    PC_AUDIO_OK = 0,
    PC_AUDIO_ERROR_NATIVE_INIT = 1,
    PC_AUDIO_ERROR_PERMISSION = 2,
    PC_AUDIO_ERROR_INPUT_NOT_FOUND = 3,
    PC_AUDIO_ERROR_OUTPUT_NOT_FOUND = 4,
    PC_AUDIO_ERROR_INPUT_START_FAILED = 5,
    PC_AUDIO_ERROR_OUTPUT_START_FAILED = 6,
    PC_AUDIO_ERROR_FORMAT_UNSUPPORTED = 7
} pc_audio_result;

typedef void (*pc_capture_callback)(const float* samples, int frame_count, int channels, void* user_data);
typedef int (*pc_playback_callback)(float* samples, int frame_count, int channels, void* user_data);

pc_audio_result pc_audio_init(void);
void pc_audio_shutdown(void);
int pc_audio_get_input_count(void);
int pc_audio_get_output_count(void);
int pc_audio_get_input_name(int index, char* buffer, int buffer_len);
int pc_audio_get_output_name(int index, char* buffer, int buffer_len);
int pc_audio_get_input_id(int index, char* buffer, int buffer_len);
int pc_audio_get_output_id(int index, char* buffer, int buffer_len);
pc_audio_result pc_audio_start_full_duplex(
    const char* input_id,
    const char* output_id,
    int sample_rate,
    int input_channels,
    int output_channels,
    pc_capture_callback capture_callback,
    pc_playback_callback playback_callback,
    void* user_data);
void pc_audio_stop_full_duplex(void);
int pc_audio_get_last_error(char* buffer, int buffer_len);
int pc_audio_get_stats(char* json_buffer, int buffer_len);

#ifdef __cplusplus
}
#endif
