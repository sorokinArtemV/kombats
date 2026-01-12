FROM rabbitmq:3.12-management

# Скачиваем .ez (community plugin) и кладём в plugins dir
ADD https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.12.0/rabbitmq_delayed_message_exchange-3.12.0.ez /opt/rabbitmq/plugins/

# Включаем плагин на этапе сборки
RUN rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange

