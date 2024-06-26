import json
import azure.functions as func

apis = func.Blueprint()


@apis.function_name("CreateAssistant")
@apis.route(route="assistants/{assistantId}", methods=["PUT"])
@apis.generic_output_binding(arg_name="requests", type="assistantCreate", data_type=func.DataType.STRING)
def create_assistant(req: func.HttpRequest, requests: func.Out[str]) -> func.HttpResponse:
    assistantId = req.route_params.get("assistantId")
    instructions = """
            Don't make assumptions about what values to plug into functions.
            Ask for clarification if a user request is ambiguous.
            """
    create_request = {
        "id": assistantId,
        "instructions": instructions
    }
    requests.set(json.dumps(create_request))
    response_json = {"assistantId": assistantId}
    return func.HttpResponse(json.dumps(response_json), status_code=202, mimetype="application/json")


@apis.function_name("PostUserQuery")
@apis.route(route="assistants/{assistantId}", methods=["POST"])
@apis.generic_input_binding(arg_name="state", type="assistantPost", data_type=func.DataType.STRING, id="{assistantId}", userMessage="{Query.message}", model="%CHAT_MODEL_DEPLOYMENT_NAME%")
def post_user_response(req: func.HttpRequest, state: str) -> func.HttpResponse:
    # Parse the JSON string into a dictionary
    data = json.loads(state)

    # Extract the content of the recentMessage
    recent_message_content = data['recentMessages'][0]['content']
    return func.HttpResponse(recent_message_content, status_code=200, mimetype="text/plain")


@apis.function_name("GetChatState")
@apis.route(route="assistants/{assistantId}", methods=["GET"])
@apis.generic_input_binding(arg_name="state", type="assistantQuery", data_type=func.DataType.STRING, id="{assistantId}", timestampUtc="{Query.timestampUTC}")
def get_chat_state(req: func.HttpRequest, state: str) -> func.HttpResponse:
    return func.HttpResponse(state, status_code=200, mimetype="application/json")
