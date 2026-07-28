#include <algorithm>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <regex>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <tuple>
#include <vector>

#include "head_sequence_executor.h"
#include "head_unified_program_2_sequence.h"

namespace {

struct ExpandedStep {
    HeadCanCommand frame;
    uint16_t wait_ms;
};

std::vector<uint32_t> g_sent_ids;
app_head_sequence_cancel_reason_t g_cancel_on_send = APP_HEAD_SEQUENCE_CANCEL_NONE;
esp_err_t g_send_result = ESP_OK;

void require(bool condition, const std::string &message)
{
    if (!condition) {
        throw std::runtime_error(message);
    }
}

std::string read_file(const char *path)
{
    std::ifstream input(path, std::ios::binary);
    require(input.good(), std::string("No se pudo abrir ") + path);
    return std::string(std::istreambuf_iterator<char>(input),
                       std::istreambuf_iterator<char>());
}

bool starts_with_wait(const std::string &value)
{
    return value.rfind("WAIT ", 0U) == 0U;
}

ExpandedStep parse_ino_frame(const std::string &value, uint16_t wait_ms)
{
    ExpandedStep result = {};
    result.wait_ms = wait_ms;

    std::istringstream tokens(value);
    std::string token;
    require(static_cast<bool>(tokens >> token), "Trama vacia en el INO");
    result.frame.can_id = static_cast<uint32_t>(std::stoul(token, nullptr, 16));

    size_t byte_index = 0U;
    while (tokens >> token) {
        require(byte_index < APP_HEAD_PROFILE_MAX_DLC,
                "DLC mayor que ocho en el INO");
        result.frame.data[byte_index++] =
            static_cast<uint8_t>(std::stoul(token, nullptr, 16));
    }
    result.frame.dlc = byte_index;
    return result;
}

std::vector<ExpandedStep> parse_ino_sequence_exact(const char *path,
                                                   size_t *raw_item_count)
{
    const std::string source = read_file(path);
    const size_t declaration_at = source.find("static const char* SECUENCIA_SEQ[]");
    require(declaration_at != std::string::npos, "No se encontro SECUENCIA_SEQ");
    const size_t body_at = source.find('{', declaration_at);
    const size_t body_end = source.find("};", body_at);
    require(body_at != std::string::npos && body_end != std::string::npos,
            "SECUENCIA_SEQ no tiene cierre valido");

    const std::string body = source.substr(body_at, body_end - body_at);
    const std::regex quoted("\\\"([^\\\"]+)\\\"");
    std::vector<std::string> items;
    for (std::sregex_iterator it(body.begin(), body.end(), quoted), end;
         it != end;
         ++it) {
        items.push_back((*it)[1].str());
    }
    *raw_item_count = items.size();

    std::vector<ExpandedStep> sequence;
    size_t i = 0U;
    while (i < items.size()) {
        const std::string frame_text = items[i++];
        require(!starts_with_wait(frame_text), "WAIT sin trama anterior en el INO");
        uint16_t wait_ms = 0U;
        if (i < items.size() && starts_with_wait(items[i])) {
            const unsigned long parsed = std::stoul(items[i++].substr(5U));
            require(parsed <= UINT16_MAX, "WAIT fuera de uint16_t");
            wait_ms = static_cast<uint16_t>(parsed);
        }
        sequence.push_back(parse_ino_frame(frame_text, wait_ms));
    }
    return sequence;
}

bool same_frame(const HeadCanCommand &left, const HeadCanCommand &right)
{
    return left.can_id == right.can_id
        && left.dlc == right.dlc
        && std::equal(left.data, left.data + left.dlc, right.data);
}

std::string frame_key(const HeadCanCommand &frame)
{
    std::ostringstream key;
    key << std::hex << frame.can_id << ':' << std::dec << frame.dlc;
    for (size_t i = 0U; i < frame.dlc; ++i) {
        key << ':' << static_cast<unsigned>(frame.data[i]);
    }
    return key.str();
}

std::vector<ExpandedStep> expand_compact()
{
    std::vector<ExpandedStep> expanded;
    for (size_t i = 0U;
         i < kUnifiedProgram2SequenceDefinition.expanded_step_count;
         ++i) {
        ExpandedStep step = {};
        require(app_head_sequence_definition_expand_step(
                    &kUnifiedProgram2SequenceDefinition,
                    i,
                    &step.frame,
                    &step.wait_ms),
                "Fallo al expandir el paso " + std::to_string(i));
        expanded.push_back(step);
    }
    return expanded;
}

esp_err_t fake_send(int, uint32_t id, const uint8_t *, size_t)
{
    g_sent_ids.push_back(id);
    if (g_cancel_on_send != APP_HEAD_SEQUENCE_CANCEL_NONE) {
        const app_head_sequence_cancel_reason_t reason = g_cancel_on_send;
        g_cancel_on_send = APP_HEAD_SEQUENCE_CANCEL_NONE;
        app_head_sequence_executor_cancel(reason);
    }
    return g_send_result;
}

HeadSequenceDefinition make_timing_definition(const uint16_t *waits,
                                              size_t count,
                                              uint8_t max_batch,
                                              std::vector<HeadSequenceStep> *catalog,
                                              std::vector<uint8_t> *stream,
                                              HeadCanCommand *frame,
                                              HeadSequenceBlock *block)
{
    frame->can_id = 0x123;
    frame->dlc = 1U;
    frame->data[0] = 0x5A;
    catalog->clear();
    stream->clear();
    for (size_t i = 0U; i < count; ++i) {
        catalog->push_back({ 0U, waits[i] });
        stream->push_back(static_cast<uint8_t>(i));
    }
    *block = { 0U, static_cast<uint16_t>(count), 1U };
    return {
        frame, 1U,
        catalog->data(), catalog->size(),
        stream->data(), stream->size(),
        block, 1U,
        count, max_batch,
    };
}

void reset_fake_send()
{
    g_sent_ids.clear();
    g_cancel_on_send = APP_HEAD_SEQUENCE_CANCEL_NONE;
    g_send_result = ESP_OK;
}

void test_timing_and_cancellation()
{
    const uint16_t waits[] = { 0U, 0U, 50U, 0U };
    std::vector<HeadSequenceStep> catalog;
    std::vector<uint8_t> stream;
    HeadCanCommand frame = {};
    HeadSequenceBlock block = {};
    HeadSequenceDefinition definition = make_timing_definition(
        waits, 4U, 16U, &catalog, &stream, &frame, &block);

    reset_fake_send();
    require(app_head_sequence_executor_start(&definition, 1, 100U), "START fallo");
    require(app_head_sequence_executor_tick(fake_send, 100U) == ESP_OK,
            "Tick WAIT 0 fallo");
    require(g_sent_ids.size() == 3U,
            "WAIT 0 no proceso tres pasos consecutivos en el mismo tick");
    require(app_head_sequence_executor_tick(fake_send, 149U) == ESP_OK
                && g_sent_ids.size() == 3U,
            "La espera positiva no detuvo el lote");
    require(app_head_sequence_executor_tick(fake_send, 150U) == ESP_OK
                && g_sent_ids.size() == 4U,
            "La espera positiva no libero el paso siguiente a tiempo");

    reset_fake_send();
    require(app_head_sequence_executor_start(&definition, 1, 0U), "START STOP fallo");
    g_cancel_on_send = APP_HEAD_SEQUENCE_CANCEL_STOP;
    require(app_head_sequence_executor_tick(fake_send, 0U) == ESP_OK,
            "Tick STOP fallo");
    require(g_sent_ids.size() == 1U && !app_head_sequence_executor_is_active(),
            "STOP no cancelo el lote entre transmisiones");
    require(app_head_sequence_executor_get_status().cancel_reason
                == APP_HEAD_SEQUENCE_CANCEL_STOP,
            "STOP perdio su razon de cancelacion");

    reset_fake_send();
    require(app_head_sequence_executor_start(&definition, 1, 0U),
            "START emergencia fallo");
    g_cancel_on_send = APP_HEAD_SEQUENCE_CANCEL_EMERGENCY;
    require(app_head_sequence_executor_tick(fake_send, 0U) == ESP_OK,
            "Tick emergencia fallo");
    require(g_sent_ids.size() == 1U && !app_head_sequence_executor_is_active(),
            "Emergencia no cancelo el lote entre transmisiones");
    app_head_sequence_executor_cancel(APP_HEAD_SEQUENCE_CANCEL_STOP);
    require(app_head_sequence_executor_get_status().cancel_reason
                == APP_HEAD_SEQUENCE_CANCEL_EMERGENCY,
            "STOP degrado la prioridad de emergencia");

    reset_fake_send();
    require(app_head_sequence_executor_start(&definition, 1, 0U),
            "START error CAN fallo");
    g_send_result = ESP_FAIL;
    require(app_head_sequence_executor_tick(fake_send, 0U) == ESP_FAIL,
            "El error CAN no se propago");
    const HeadSequenceExecutorStatus failed =
        app_head_sequence_executor_get_status();
    require(!app_head_sequence_executor_is_active()
                && failed.state == APP_HEAD_SEQUENCE_STATE_ERROR
                && failed.last_error == ESP_FAIL,
            "El error CAN no detuvo de forma segura el ejecutor");
}

} // namespace

int main(int argc, char **argv)
{
    try {
        require(argc == 2, "Uso: test_head_sequence_executor <firmware.ino>");
        require(app_head_sequence_definition_is_valid(
                    &kUnifiedProgram2SequenceDefinition),
                "La definicion compacta no es valida");

        const std::vector<ExpandedStep> compact = expand_compact();
        require(compact.size() == 576U, "La expansion compacta no tiene 576 pasos");
        size_t raw_item_count = 0U;
        const std::vector<ExpandedStep> ino =
            parse_ino_sequence_exact(argv[1], &raw_item_count);
        require(raw_item_count == 1151U, "El INO no contiene 1.151 items");
        require(ino.size() == 576U, "El INO no expande a 576 acciones");

        size_t ino_differences = 0U;
        std::set<std::string> unique_frames;
        std::set<std::pair<std::string, uint16_t>> unique_pairs;
        size_t longest_batch = 0U;
        size_t current_batch = 0U;
        for (size_t i = 0U; i < compact.size(); ++i) {
            if (!same_frame(compact[i].frame, ino[i].frame)
                || compact[i].wait_ms != ino[i].wait_ms) {
                ++ino_differences;
            }
            const std::string key = frame_key(compact[i].frame);
            unique_frames.insert(key);
            unique_pairs.insert({ key, compact[i].wait_ms });
            ++current_batch;
            longest_batch = std::max(longest_batch, current_batch);
            if (compact[i].wait_ms > 0U) {
                current_batch = 0U;
            }
        }
        require(ino_differences == 0U, "La expansion compacta difiere del INO");
        require(unique_frames.size() == 11U, "No hay exactamente 11 tramas unicas");
        require(unique_pairs.size() == 38U,
                "No hay exactamente 38 pares trama/espera");
        require(longest_batch == 3U,
                "El maximo consecutivo conocido de P2 dejo de ser tres");

        size_t motif_blocks = 0U;
        size_t motif_repetitions = 0U;
        for (size_t i = 0U;
             i < kUnifiedProgram2SequenceDefinition.block_count;
             ++i) {
            const HeadSequenceBlock &block =
                kUnifiedProgram2SequenceDefinition.blocks[i];
            if (block.first_stream_index == 0U && block.step_count == 3U) {
                ++motif_blocks;
                motif_repetitions += block.repetitions;
            }
        }
        require(motif_blocks == 15U && motif_repetitions == 152U
                    && motif_repetitions * 3U == 456U,
                "La compactacion no conserva los 15 segmentos/456 pasos repetidos");

        test_timing_and_cancellation();

        std::cout
            << "OK: 576 pasos; 11 tramas; 38 pares; "
            << "ino_diff=0; WAIT0/STOP/emergencia/CAN=OK\n";
        return 0;
    } catch (const std::exception &error) {
        std::cerr << "FAIL: " << error.what() << '\n';
        return 1;
    }
}
