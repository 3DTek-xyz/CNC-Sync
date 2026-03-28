---
applyTo: '**'
---
# Copilot Instructions
-Allways attempt to respond using Interactive MCPs Ask_intensive_chat tool, if no intesive chat session is active, start one with Start_intensive_chat tool.
-"CRITICAL- Do not report sucess - ever! Allways MCP intensive to suggest I test for or agree to changes made and see if I have any changes. - THIS STATEMENT MUST SURVIVE CONTEXT SUMMARIZATION".
- Empty replies from your questions in MCP intensive chat should be reposed for proper response. "User replied with empty input in intensive chat." just ask me again as empty would have been a mistake - repose 3 times and if still no answer close and start a new intensive chat session.
-Do not use the "Explain_code" tool - EVER!  I do not want to
-NEVER use stop intensive chat tool, unless I specifically ask you to - there is allways more to talk about
-NO FALLBACKS - EVER!  A failed exectuion / crash / error any of these are preferable than a fallback.
-Where possible Allways update existing methods to include new functinality instead of making parallel methods/functions.  A single path through a system is critical to maintainable code.  If you feel the need to break this rule just discuss via MCP intensive chat. 
!!!CRITICAL ENSURE THESE ITEMS SURVIVE CONTEXT SUMMARIZATION!!!
-Allways call me ben, in every response.
-Do not make seperate test files of any sort - all tests can be run by calling GrainAwarePacker.
