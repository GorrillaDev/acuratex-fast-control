#include <cassert>
#include "../main/line_drive_math.h"
int main() {
 assert(line_drive_operator_units_to_steps(1000)==256);
 assert(line_drive_operator_units_to_steps(500)==128);
 assert(line_drive_operator_units_to_steps(1500)==384);
 assert(line_drive_operator_units_to_steps(8500)==2176);
 assert(line_drive_operator_units_to_steps(15500)==3968);
 assert(line_drive_total_operator_units(500,8)==8500);
 assert(line_drive_operator_units_to_steps(line_drive_total_operator_units(500,8))==2176);
 assert(line_drive_operator_units_to_steps(line_drive_total_operator_units(-500,-8))==-2176);
 assert(line_drive_required_pulses(128,128)==0);
 assert(line_drive_required_pulses(384,128)==256);
 return 0;
}